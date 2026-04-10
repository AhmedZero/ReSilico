// ReSilico – Probabilistic Damage and Loss Assessment Engine
// ConvergenceDiagnostic: per-iteration snapshot from the adaptive loop

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Statistical snapshot captured after each adaptive iteration.
///
/// The full history is available via <see cref="AdaptiveSimulationResult.Diagnostics"/>.
/// </summary>
/// <param name="Iteration">1-based iteration index.</param>
/// <param name="TotalSamples">Cumulative number of Monte Carlo realisations so far.</param>
/// <param name="MeanCost">Running mean of total repair cost across all accumulated samples.</param>
/// <param name="StdDevCost">Running sample standard deviation of total repair cost.</param>
/// <param name="PercentileCost">Empirical <see cref="TargetQuantile"/> percentile of total repair cost.</param>
/// <param name="CVaR">
/// Conditional Value-at-Risk (tail mean) at <see cref="TargetQuantile"/>:
/// the mean cost of realisations that exceed the quantile threshold.
/// </param>
/// <param name="MeanRelativeError">
/// Relative standard error of the mean: SE(mean) / |mean| = σ / (√N · |μ|).
/// Convergence condition 1: must fall below <see cref="AdaptiveOptions.ToleranceMean"/>.
/// </param>
/// <param name="TailRelativeChange">
/// Iteration-to-iteration relative change of CVaR:
/// |CVaR_new − CVaR_prev| / |CVaR_prev|.
/// +∞ for the first iteration (no previous estimate available).
/// Convergence condition 2: must fall below <see cref="AdaptiveOptions.ToleranceTail"/>.
/// </param>
public sealed record ConvergenceDiagnostic(
    int Iteration,
    int TotalSamples,
    double MeanCost,
    double StdDevCost,
    double PercentileCost,
    double CVaR,
    double MeanRelativeError,
    double TailRelativeChange);
