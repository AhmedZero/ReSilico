// ReSilico – Probabilistic Damage and Loss Assessment Engine
// FragilityFunction: lognormal fragility P(DS ≥ ds | EDP) with batch evaluation

using System.Buffers;
using System.Numerics;
using ReSilico.Core.Distributions;

namespace ReSilico.Analysis.Damage;

/// <summary>
/// Lognormal fragility function for a single limit state:
///
///   P(DS ≥ ds | EDP = x) = Φ( (ln(x) − ln(θ)) / β )
///
/// where:
///   θ  = median EDP capacity (50% exceedance point)
///   β  = total dispersion (log-standard deviation)
///   Φ  = standard normal CDF
///
/// This is the FEMA P-58 / HAZUS fragility formulation.
///
/// Performance:
///   • <see cref="BatchEvaluate"/> vectorizes the z-score
///     z = (ln(x) − ln(θ)) / β  using SIMD (via <see cref="Vector{T}"/>)
///     and then applies the scalar erf approximation per element.
///   • Pre-computed constants avoid repeated division in hot loops.
///   • All objects are effectively immutable (readonly fields).
/// </summary>
public sealed class FragilityFunction
{
    private readonly LognormalDistribution _dist;

    // Pre-computed for hot-path evaluation.
    private readonly double _lnMedian;      // ln(θ)
    private readonly double _betaRecip;     // 1/β    for scalar scalar
    private readonly double _cdfScaleRecip; // 1/(β√2) for the erf CDF form

    public FragilityFunction(double median, double beta, string limitStateLabel = "")
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(median, 0d, nameof(median));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(beta, 0d, nameof(beta));

        Median = median;
        Beta = beta;
        LimitStateLabel = limitStateLabel;

        _dist = LognormalDistribution.FromMedianAndDispersion(median, beta);
        _lnMedian = Math.Log(median);
        _betaRecip = 1.0 / beta;
        _cdfScaleRecip = 1.0 / (beta * Math.Sqrt(2.0));
    }

    // ── Properties (immutable) ───────────────────────────────────────────────

    public double Median { get; }
    public double Beta { get; }
    public string LimitStateLabel { get; }

    // ── Scalar evaluation ─────────────────────────────────────────────────────

    /// <summary>P(DS ≥ ds | EDP = edp) for a scalar EDP value.</summary>
    public double Evaluate(double edp)
    {
        if (edp <= 0.0) return 0.0;
        return _dist.CDF(edp);
    }

    /// <summary>z-score: (ln(EDP) − ln(θ)) / β.</summary>
    public double ZScore(double edp) =>
        edp > 0.0 ? (Math.Log(edp) - _lnMedian) * _betaRecip : double.NegativeInfinity;

    /// <summary>
    /// True if this limit state is exceeded for the given EDP and a uniform
    /// capacity variate u ~ U(0,1).
    /// Exceeded iff u &lt; P(DS ≥ ds | EDP).
    /// </summary>
    public bool IsExceeded(double edp, double uniformCapacity)
    {
        if (edp <= 0.0) return false;
        return uniformCapacity < Evaluate(edp);
    }

    // ── Batch evaluation — SIMD z-score + scalar Φ ───────────────────────────

    /// <summary>
    /// Evaluate fragility for a batch of EDP values.
    /// Writes exceedance probabilities into <paramref name="result"/>.
    ///
    /// Algorithm (for non-negative EDP):
    ///   1. lnEdp[i] = ln(edp[i])           — scalar
    ///   2. z[i] = (lnEdp[i] − lnθ) / (β√2) — SIMD vectorized
    ///   3. p[i] = 0.5 · (1 + erf(z[i]))    — scalar erf
    /// </summary>
    public void BatchEvaluate(ReadOnlySpan<double> edpSamples, Span<double> result)
    {
        if (edpSamples.Length != result.Length)
            throw new ArgumentException("edpSamples and result must be the same length.");

        int n = edpSamples.Length;

        double[] lnBuf = ArrayPool<double>.Shared.Rent(n);
        double[] zBuf = ArrayPool<double>.Shared.Rent(n);
        try
        {
            // Step 1: scalar ln pass.
            for (int i = 0; i < n; i++)
                lnBuf[i] = edpSamples[i] > 0.0 ? Math.Log(edpSamples[i]) : double.NegativeInfinity;

            // Step 2: SIMD z-score  z = (ln(EDP) − ln(θ)) · (1/(β√2))
            int i2 = 0;
            if (Vector.IsHardwareAccelerated)
            {
                int vLen = Vector<double>.Count;
                var vLnTheta = new Vector<double>(_lnMedian);
                var vScale = new Vector<double>(_cdfScaleRecip);

                for (; i2 <= n - vLen; i2 += vLen)
                {
                    var vlnEdp = new Vector<double>(lnBuf.AsSpan(i2));
                    var vz = (vlnEdp - vLnTheta) * vScale;
                    vz.CopyTo(zBuf.AsSpan(i2));
                }
            }
            for (; i2 < n; i2++)
                zBuf[i2] = (lnBuf[i2] - _lnMedian) * _cdfScaleRecip;

            // Step 3: scalar erf pass.
            for (int i = 0; i < n; i++)
            {
                double z = zBuf[i];
                result[i] = double.IsNegativeInfinity(z) ? 0.0 : 0.5 * (1.0 + FastErf(z));
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(lnBuf);
            ArrayPool<double>.Shared.Return(zBuf);
        }
    }

    /// <summary>
    /// Determine which realisations exceed this limit state, writing boolean
    /// results as 1.0 (exceeded) or 0.0 (not exceeded) into <paramref name="exceeded"/>.
    /// Uses <see cref="BatchEvaluate"/> for the fragility, then compares with
    /// per-realisation capacity samples.
    /// </summary>
    public void BatchIsExceeded(
        ReadOnlySpan<double> edpSamples,
        ReadOnlySpan<double> capacitySamples,
        Span<bool> exceeded)
    {
        int n = edpSamples.Length;
        double[] pBuf = ArrayPool<double>.Shared.Rent(n);
        try
        {
            BatchEvaluate(edpSamples, pBuf.AsSpan(0, n));
            for (int i = 0; i < n; i++)
                exceeded[i] = capacitySamples[i] < pBuf[i];
        }
        finally
        {
            ArrayPool<double>.Shared.Return(pBuf);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    // Abramowitz & Stegun 7.1.26 erf approximation — max |ε| < 1.5 × 10⁻⁷.
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

    public override string ToString() =>
        $"Fragility[θ={Median:G4}, β={Beta:G4}] {LimitStateLabel}";
}
