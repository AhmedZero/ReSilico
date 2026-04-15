// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SensitivityOptions: configuration for sensitivity analysis runs

using ReSilico.Domain.Enums;

namespace ReSilico.Analysis.Sensitivity;

/// <summary>
/// Statistical measure on which sensitivity is evaluated.
/// The selected measure is computed on the output sample produced by each
/// pipeline run; the sensitivity index quantifies how that measure responds
/// to parameter perturbations.
/// </summary>
public enum SensitivityStatistic
{
    /// <summary>Expected (mean) value of the selected decision variable.</summary>
    Mean,

    /// <summary>Standard deviation of the selected decision variable.</summary>
    StdDev,

    /// <summary>
    /// Quantile (percentile) of the loss distribution.
    /// Uses <see cref="SensitivityOptions.TargetQuantile"/>.
    /// </summary>
    Percentile,

    /// <summary>
    /// Conditional Value at Risk (CVaR / Expected Shortfall).
    /// The most discriminating measure for tail-risk sensitivity.
    /// Uses <see cref="SensitivityOptions.TargetQuantile"/>.
    /// </summary>
    CVaR,
}

/// <summary>
/// Configuration for a <see cref="SensitivityAnalyzer"/> run.
///
/// All perturbable parameters in the pipeline are enumerated automatically;
/// these options control the perturbation magnitude, sample size, and
/// which output statistic to differentiate.
/// </summary>
public sealed class SensitivityOptions
{
    /// <summary>
    /// Number of Monte Carlo samples per simulation run.
    /// All baseline and perturbed runs use this fixed sample size so that
    /// comparisons are unaffected by convergence differences.
    /// Default: 20 000.
    /// </summary>
    public int SampleSize { get; set; } = 20_000;

    /// <summary>
    /// Relative perturbation factor ε for finite-difference and sweep methods.
    /// Parameters are perturbed to θ×(1±ε).  For correlation coefficients the
    /// same value is used as an absolute delta on ρ.
    /// Default: 0.05 (±5 %).
    /// </summary>
    public double PerturbationFactor { get; set; } = 0.05;

    /// <summary>
    /// Algorithmic strategy:
    /// <see cref="SensitivityMethod.Local"/>  — centered finite difference (one parameter at a time).
    /// <see cref="SensitivityMethod.Global"/> — Morris elementary-effects sweep.
    /// <see cref="SensitivityMethod.Correlation"/> — per-pair correlation perturbation.
    /// Default: <see cref="SensitivityMethod.Local"/>.
    /// </summary>
    public SensitivityMethod Method { get; set; } = SensitivityMethod.Local;

    /// <summary>
    /// Decision variable on which the target statistic is computed.
    /// Currently only <see cref="DecisionVariable.Cost"/> is fully supported.
    /// Default: <see cref="DecisionVariable.Cost"/>.
    /// </summary>
    public DecisionVariable TargetMetric { get; set; } = DecisionVariable.Cost;

    /// <summary>
    /// Statistical measure to differentiate with respect to each parameter.
    /// Default: <see cref="SensitivityStatistic.CVaR"/> — the most informative
    /// measure for identifying drivers of tail risk.
    /// </summary>
    public SensitivityStatistic TargetStatistic { get; set; } = SensitivityStatistic.CVaR;

    /// <summary>
    /// Quantile level p ∈ (0, 1) used when
    /// <see cref="TargetStatistic"/> is <see cref="SensitivityStatistic.Percentile"/>
    /// or <see cref="SensitivityStatistic.CVaR"/>.
    /// Default: 0.95 (95th percentile / CVaR₉₅).
    /// </summary>
    public double TargetQuantile { get; set; } = 0.95;

    /// <summary>
    /// Number of evenly-spaced parameter levels for the Global (Morris) sweep.
    /// Must be ≥ 3.  A higher value captures non-linearity better at the cost of
    /// more simulation runs.
    /// Default: 5.
    /// </summary>
    public int GlobalLevels { get; set; } = 5;

    /// <summary>
    /// Master RNG seed for reproducibility.
    /// All pipeline runs derive their seeds from this value.
    /// −1 = clock-based (non-deterministic).
    /// Default: 42.
    /// </summary>
    public int Seed { get; set; } = 42;
}
