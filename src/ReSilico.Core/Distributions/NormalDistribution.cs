// ReSilico – Probabilistic Damage and Loss Assessment Engine
// NormalDistribution: N(μ, σ²) with SIMD-accelerated batch operations

using System.Buffers;
using System.Numerics;
using Numerics.Distributions;

namespace ReSilico.Core.Distributions;

/// <summary>
/// Normal (Gaussian) distribution N(μ, σ²).
///
/// Inherits from <see cref="DistributionBase"/> which provides:
///   • Batch sampling via inverse-transform + ArrayPool.
///   • SIMD z-score helper via <c>VectorizedZScore</c>.
///
/// This class overrides <see cref="BatchCDF"/> to vectorize the linear z-score
/// computation (z = (x − μ) / σ) using <see cref="Vector{T}"/> before applying
/// the scalar error-function for each element.
///
/// Thread safety: all state is readonly after construction.
/// </summary>
public sealed class NormalDistribution : DistributionBase
{
    private readonly Normal _inner;
    private readonly double _sigmaRecip;            // 1/σ  (avoid repeated division)
    private readonly double _cdfScaleRecip;         // 1/(σ√2)  for erf-based CDF

    public NormalDistribution(double mean, double standardDeviation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(standardDeviation, 0d, nameof(standardDeviation));
        _inner = new Normal(mean, standardDeviation);
        _sigmaRecip = 1.0 / standardDeviation;
        _cdfScaleRecip = 1.0 / (standardDeviation * Math.Sqrt(2.0));
    }

    // -------------------------------------------------------------------------
    // Properties

    public override string Name => "Normal";
    public override double Mean => _inner.Mu;
    public override double Variance => _inner.Sigma * _inner.Sigma;
    public double Mu => _inner.Mu;
    public double Sigma => _inner.Sigma;

    // -------------------------------------------------------------------------
    // Scalar primitives

    public override double PDF(double x) => _inner.PDF(x);
    public override double CDF(double x) => _inner.CDF(x);
    public override double InverseCDF(double probability) => _inner.InverseCDF(probability);
    public override IUnivariateDistribution Clone() => new NormalDistribution(_inner.Mu, _inner.Sigma);

    // -------------------------------------------------------------------------
    // Batch CDF — vectorized z-score + scalar erf

    /// <summary>
    /// Evaluate CDF for a batch of inputs using SIMD-vectorized z-score computation.
    /// CDF(x) = 0.5 · (1 + erf((x − μ) / (σ√2)))
    ///
    /// The linear z-score (x − μ) / (σ√2) is computed with Vector&lt;double&gt; SIMD.
    /// The erf per element remains scalar (no .NET SIMD erf intrinsic exists).
    /// </summary>
    public override void BatchCDF(ReadOnlySpan<double> x, Span<double> result)
    {
        if (x.Length != result.Length)
            throw new ArgumentException("x and result must be the same length.");

        // Use the SIMD helper to compute z[i] = (x[i] − μ) / (σ√2)
        double[] zBuf = ArrayPool<double>.Shared.Rent(x.Length);
        try
        {
            VectorizedZScore(x, zBuf.AsSpan(0, x.Length), _inner.Mu, _cdfScaleRecip);
            for (int i = 0; i < x.Length; i++)
                result[i] = 0.5 * (1.0 + Erf(zBuf[i]));
        }
        finally
        {
            ArrayPool<double>.Shared.Return(zBuf);
        }
    }

    // -------------------------------------------------------------------------
    // Standard normal helpers (static — no allocation)

    /// <summary>Standard normal CDF: Φ(z).</summary>
    public static double StandardCDF(double z) => Normal.StandardCDF(z);

    /// <summary>Inverse standard normal CDF: Φ⁻¹(p).</summary>
    public static double StandardInverseCDF(double p) => Normal.StandardZ(p);

    // -------------------------------------------------------------------------
    // High-performance batch standard-normal CDF

    /// <summary>
    /// Evaluate Φ(z) for a batch, using SIMD for z-score computation.
    /// Input z values are assumed to already be standard-normal quantiles.
    /// </summary>
    public static void BatchStandardCDF(ReadOnlySpan<double> z, Span<double> result)
    {
        int i = 0;
        double sqrt2Recip = 1.0 / Math.Sqrt(2.0);

        if (Vector.IsHardwareAccelerated)
        {
            int vLen = Vector<double>.Count;
            var vScale = new Vector<double>(sqrt2Recip);

            for (; i <= z.Length - vLen; i += vLen)
            {
                var vz = new Vector<double>(z[i..]) * vScale;

                for (int j = 0; j < vLen; j++)
                {
                    result[i + j] = 0.5 * (1.0 + Erf(vz[j]));
                }
            }
        }

        for (; i < z.Length; i++)
            result[i] = 0.5 * (1.0 + Erf(z[i] * sqrt2Recip));
    }

    // -------------------------------------------------------------------------
    // Approximation of the error function (Abramowitz & Stegun 7.1.26, max |ε| < 1.5e-7)
    // Avoids the overhead of calling into Numerics special functions for tight batch loops.

    private static double Erf(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        double sign = x < 0 ? -1.0 : 1.0;
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
