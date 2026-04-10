// ReSilico – Probabilistic Damage and Loss Assessment Engine
// IncrementalStats: Welford's online mean/variance algorithm

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Numerically stable online mean and variance using Welford's one-pass algorithm.
///
/// Properties:
///   • O(1) per sample — no need to re-scan past data.
///   • Numerically stable (avoids catastrophic cancellation in naive sum-of-squares).
///   • Not thread-safe; protect externally if needed.
///
/// Reference: B. P. Welford (1962), "Note on a method for calculating corrected
/// sums of squares and products", Technometrics 4(3), 419–420.
/// </summary>
internal sealed class IncrementalStats
{
    private long   _n;
    private double _mean;
    private double _m2;    // running sum of squared deviations: Σ(xᵢ − mean)²

    // ── Ingestion ─────────────────────────────────────────────────────────────

    /// <summary>Update the running statistics with a single new observation.</summary>
    public void Add(double value)
    {
        _n++;
        double delta  = value - _mean;
        _mean        += delta / _n;
        _m2          += delta * (value - _mean);   // uses updated mean intentionally
    }

    /// <summary>Update the running statistics with a batch of new observations.</summary>
    public void AddRange(ReadOnlySpan<double> values)
    {
        foreach (var v in values) Add(v);
    }

    // ── Derived statistics ────────────────────────────────────────────────────

    /// <summary>Number of observations seen so far.</summary>
    public long Count => _n;

    /// <summary>Running sample mean.</summary>
    public double Mean => _mean;

    /// <summary>
    /// Unbiased sample variance (denominator n−1).
    /// Returns 0 when fewer than 2 observations have been added.
    /// </summary>
    public double Variance => _n < 2 ? 0.0 : _m2 / (_n - 1);

    /// <summary>Sample standard deviation.</summary>
    public double StdDev => Math.Sqrt(Variance);

    /// <summary>
    /// Relative standard error of the mean: σ / (√n · |μ|).
    ///
    /// Returns <see cref="double.PositiveInfinity"/> when:
    ///   • Fewer than 2 observations (variance undefined), or
    ///   • Mean is effectively zero (relative error is infinite).
    ///
    /// This is the primary convergence metric for the mean.
    /// </summary>
    public double RelativeMeanError =>
        _n < 2 || Math.Abs(_mean) < double.Epsilon
            ? double.PositiveInfinity
            : StdDev / (Math.Sqrt(_n) * Math.Abs(_mean));
}
