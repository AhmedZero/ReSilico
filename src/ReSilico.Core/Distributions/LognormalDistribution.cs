// ReSilico – Probabilistic Damage and Loss Assessment Engine
// LognormalDistribution — natural-log parameterisation with vectorized batch CDF

using System.Buffers;
using System.Numerics;
using Numerics.Distributions;

namespace ReSilico.Core.Distributions;

/// <summary>
/// Lognormal distribution with natural-log parameterisation.
///
///   X ~ Lognormal(μ_ln, β)  ⟺  ln(X) ~ N(μ_ln, β²)
///
/// Fragility convention:
///   P(DS ≥ ds | EDP = x) = Φ( (ln(x) − μ_ln) / β )
///   where μ_ln = ln(median) and β = log-standard deviation (dispersion).
///
/// Performance:
///   • <see cref="BatchCDF"/> vectorizes the z-score
///     z = (ln(x) − μ_ln) / (β√2)  using SIMD (non-log part) and scalar ln.
///   • Uses <see cref="ArrayPool{T}"/> for all temporary allocations.
///   • All fields are readonly — effectively immutable.
///
/// Thread safety: safe for concurrent reads; no mutable state after construction.
/// </summary>
public sealed class LognormalDistribution : DistributionBase
{
    private readonly LogNormal _inner;
    private readonly double _cdfScaleRecip;   // 1 / (β√2)

    /// <param name="muLn">μ_ln — mean of ln(X). Equal to ln(median).</param>
    /// <param name="sigmaLn">β — std-dev of ln(X), the dispersion parameter (> 0).</param>
    public LognormalDistribution(double muLn, double sigmaLn)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sigmaLn, 0d, nameof(sigmaLn));
        _inner = new LogNormal(muLn, sigmaLn) { Base = Math.E };
        _cdfScaleRecip = 1.0 / (sigmaLn * Math.Sqrt(2.0));
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Create from the real-space median θ and dispersion β.
    /// μ_ln is automatically computed as ln(θ).
    /// </summary>
    public static LognormalDistribution FromMedianAndDispersion(double median, double beta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(median, 0d, nameof(median));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(beta, 0d, nameof(beta));
        return new LognormalDistribution(Math.Log(median), beta);
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public override string Name => "Lognormal";

    /// <summary>μ_ln — mean of ln(X).</summary>
    public double MuLn => _inner.Mu;

    /// <summary>β — dispersion (std-dev of ln(X)).</summary>
    public double SigmaLn => _inner.Sigma;

    /// <summary>Median of X = exp(μ_ln).</summary>
    public double Median => Math.Exp(_inner.Mu);

    public override double Mean => _inner.Mean;
    public override double Variance => _inner.Variance;

    // ── Scalar primitives ─────────────────────────────────────────────────────

    public override double PDF(double x) => _inner.PDF(x);
    public override double CDF(double x) => _inner.CDF(x);
    public override double InverseCDF(double probability) => _inner.InverseCDF(probability);
    public override IUnivariateDistribution Clone() => new LognormalDistribution(_inner.Mu, _inner.Sigma);

    // ── Batch CDF — vectorized z-score (after scalar ln) ─────────────────────

    /// <summary>
    /// Evaluate CDF for a batch of inputs.
    ///
    /// Algorithm:
    ///   1. ln(x[i]) — scalar (no hardware ln intrinsic in Vector&lt;double&gt;)
    ///   2. z[i] = (ln(x[i]) − μ_ln) / (β√2) — vectorized SIMD subtraction/multiplication
    ///   3. Φ(z[i]) via scalar erf approximation
    ///
    /// Uses <see cref="ArrayPool{T}"/> for the intermediate log-space buffer.
    /// </summary>
    public override void BatchCDF(ReadOnlySpan<double> x, Span<double> result)
    {
        if (x.Length != result.Length)
            throw new ArgumentException("x and result must be the same length.");

        double[] lnBuf = ArrayPool<double>.Shared.Rent(x.Length);
        try
        {
            // Step 1: scalar ln pass.
            for (int i = 0; i < x.Length; i++)
                lnBuf[i] = x[i] > 0.0 ? Math.Log(x[i]) : double.NegativeInfinity;

            // Step 2: vectorized z-score   z = (ln(x) − μ_ln) * (1/(β√2))
            VectorizedZScore(lnBuf.AsSpan(0, x.Length), result, _inner.Mu, _cdfScaleRecip);

            // Step 3: Φ(z) via erf.
            for (int i = 0; i < result.Length; i++)
            {
                double z = result[i];
                result[i] = double.IsNegativeInfinity(z)
                    ? 0.0
                    : 0.5 * (1.0 + FastErf(z));
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(lnBuf);
        }
    }

    // ── Batch InverseCDF — vectorized log-scale linear transform ─────────────

    /// <summary>
    /// Apply the lognormal inverse CDF to a batch of uniform probabilities.
    ///
    /// Algorithm:
    ///   1. z[i] = Φ⁻¹(p[i])   (scalar — requires erfinv)
    ///   2. x[i] = exp(μ_ln + β · z[i])   — vectorized SIMD multiply + add, then exp
    ///
    /// The multiply-add in step 2 uses SIMD; the exp remains scalar.
    /// </summary>
    public override void BatchInverseCDF(ReadOnlySpan<double> probabilities, Span<double> result)
    {
        if (probabilities.Length != result.Length)
            throw new ArgumentException("probabilities and result must be the same length.");

        double[] zBuf = ArrayPool<double>.Shared.Rent(probabilities.Length);
        try
        {
            // Step 1: scalar Φ⁻¹
            for (int i = 0; i < probabilities.Length; i++)
                zBuf[i] = NormalDistribution.StandardInverseCDF(
                    Math.Clamp(probabilities[i], 1e-14, 1.0 - 1e-14));

            // Step 2: vectorized   lnX = μ_ln + β·z
            double muLn = _inner.Mu;
            double beta = _inner.Sigma;
            int i2 = 0;

            if (Vector.IsHardwareAccelerated)
            {
                int vLen = Vector<double>.Count;
                var vMu = new Vector<double>(muLn);
                var vBeta = new Vector<double>(beta);

                int n = probabilities.Length;
                for (; i2 <= n - vLen; i2 += vLen)
                {
                    var vz = new Vector<double>(zBuf.AsSpan(i2, vLen));
                    var vlnX = vMu + vBeta * vz;

                    // Scalar exp for each lane (no SIMD exp in Vector<double>).
                    for (int j = 0; j < vLen; j++)
                        result[i2 + j] = Math.Exp(vlnX[j]);
                }
            }

            // Scalar tail.
            for (; i2 < probabilities.Length; i2++)
                result[i2] = Math.Exp(muLn + beta * zBuf[i2]);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(zBuf);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    // Abramowitz & Stegun erf approximation — max |ε| < 1.5 × 10⁻⁷.
    private static double FastErf(double x)
    {
        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741;
        const double a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
        double sign = x < 0 ? -1.0 : 1.0;
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
