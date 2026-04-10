// ReSilico – Probabilistic Damage and Loss Assessment Engine
// AdaptiveOptions: configuration for adaptive Monte Carlo simulation

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Configuration for <see cref="SimulationPipeline.RunAdaptive"/>.
///
/// The adaptive loop starts with <see cref="InitialSamples"/>, then adds
/// <see cref="BatchSize"/> samples per iteration until both convergence
/// criteria are met or <see cref="MaxSamples"/> is reached.
///
/// Convergence criteria (both must be satisfied simultaneously):
///   1. Mean:  SE(mean) / |mean|  &lt;  <see cref="ToleranceMean"/>
///      — relative standard error of the running mean (Welford).
///   2. Tail:  |CVaR_new − CVaR_prev| / |CVaR_prev|  &lt;  <see cref="ToleranceTail"/>
///      — iteration-to-iteration relative change in the CVaR estimate.
///
/// At least two full iterations are always executed so that the tail
/// comparison is well-defined.
/// </summary>
public sealed class AdaptiveOptions
{
    /// <summary>Number of samples in the first (warm-up) batch. Default: 5 000.</summary>
    public int InitialSamples { get; init; } = 5_000;

    /// <summary>Additional samples added per subsequent batch. Default: 2 000.</summary>
    public int BatchSize { get; init; } = 2_000;

    /// <summary>
    /// Hard upper bound on total sample count.
    /// The loop terminates when this limit is reached, even if not converged.
    /// Default: 200 000.
    /// </summary>
    public int MaxSamples { get; init; } = 200_000;

    /// <summary>
    /// Convergence threshold on the relative standard error of the mean.
    /// Convergence requires SE(mean) / |mean| &lt; ToleranceMean.
    /// Default: 0.02 (2 %).
    /// </summary>
    public double ToleranceMean { get; init; } = 0.02;

    /// <summary>
    /// Convergence threshold on the relative change of the CVaR between
    /// consecutive iterations.
    /// Convergence requires |CVaR_new − CVaR_prev| / |CVaR_prev| &lt; ToleranceTail.
    /// Default: 0.05 (5 %).
    /// </summary>
    public double ToleranceTail { get; init; } = 0.05;

    /// <summary>
    /// Tail quantile for percentile and CVaR tracking (e.g. 0.95 = 95th percentile).
    /// Default: 0.95.
    /// </summary>
    public double TargetQuantile { get; init; } = 0.95;

    /// <summary>
    /// Master seed for fully reproducible results.
    /// Negative values → non-deterministic (system clock per batch).
    /// Default: 42.
    /// </summary>
    public int Seed { get; init; } = 42;
}
