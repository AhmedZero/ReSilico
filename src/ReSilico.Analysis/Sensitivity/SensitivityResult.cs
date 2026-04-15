// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SensitivityResult: output models for sensitivity analysis

namespace ReSilico.Analysis.Sensitivity;

/// <summary>
/// Aggregated output of a complete sensitivity analysis run.
/// </summary>
public sealed class SensitivityResult
{
    /// <summary>
    /// One entry per perturbable parameter that was successfully analysed.
    /// Order is unspecified (use <see cref="Ranked"/> for sorted output).
    /// </summary>
    public List<SensitivityEntry> Entries { get; init; } = [];

    /// <summary>
    /// Human-readable description of the target statistic
    /// (e.g., "CVaR(q=95%)" or "Mean").
    /// </summary>
    public string TargetStatistic { get; init; } = "";

    /// <summary>Output value of the target statistic for the unperturbed pipeline.</summary>
    public double BaselineValue { get; init; }

    /// <summary>
    /// Entries sorted by absolute sensitivity magnitude, largest first.
    /// Use this for bar-chart ranking of the most influential parameters.
    /// </summary>
    public IReadOnlyList<SensitivityEntry> Ranked =>
        [.. Entries.OrderByDescending(e => Math.Abs(e.SensitivityValue))];
}

/// <summary>
/// Sensitivity result for a single perturbable parameter.
///
/// Interpretation of <see cref="SensitivityValue"/> by method:
/// <list type="bullet">
///   <item>
///     <term>Local</term>
///     <description>
///       Centered finite-difference derivative ∂F/∂θ.
///       Units: [output units] / [parameter units].
///     </description>
///   </item>
///   <item>
///     <term>Global</term>
///     <description>
///       Morris μ* (mean absolute elementary effect).
///       Large μ* → parameter is influential; large σ (stored in
///       <see cref="RelativeSensitivity"/> before normalisation) → non-linear / interactive.
///     </description>
///   </item>
///   <item>
///     <term>Correlation</term>
///     <description>
///       Centered finite-difference derivative ∂F/∂ρ_ij.
///       Positive value → higher correlation increases the target metric.
///     </description>
///   </item>
/// </list>
/// </summary>
public sealed class SensitivityEntry
{
    /// <summary>
    /// Human-readable parameter label, e.g., "Fragility[C1][LS1].Median"
    /// or "Corr[PID-1-1,PID-2-1]".
    /// </summary>
    public required string Parameter { get; init; }

    /// <summary>
    /// Category of the parameter: "EDP", "Fragility", "Loss", or "Correlation".
    /// </summary>
    public required string ParameterGroup { get; init; }

    /// <summary>
    /// Primary sensitivity index (see class-level remarks for interpretation).
    /// A larger absolute value indicates greater influence on the output metric.
    /// </summary>
    public double SensitivityValue { get; init; }

    /// <summary>Output at the unperturbed baseline (same across all entries).</summary>
    public double BaselineValue { get; init; }

    /// <summary>Output when the parameter was increased: θ×(1+ε) or ρ+ε.</summary>
    public double PerturbedPlusValue { get; init; }

    /// <summary>Output when the parameter was decreased: θ×(1−ε) or ρ−ε.</summary>
    public double PerturbedMinusValue { get; init; }

    /// <summary>
    /// Alias for <see cref="PerturbedPlusValue"/> for API compatibility.
    /// </summary>
    public double PerturbedValue => PerturbedPlusValue;

    /// <summary>
    /// Relative (dimensionless) sensitivity.
    ///
    /// Local / Correlation:
    ///   Elasticity = (ΔF / F_baseline) / (2ε).
    ///   Comparable across parameters with different units and scales.
    ///   A value of 1 means a 1 % parameter change produces a 1 % output change.
    ///
    /// Global:
    ///   Approximate first-order Sobol index S_i ≈ Var_i / Var_total.
    ///   Values in [0, 1]; sum across all parameters ≈ 1.
    /// </summary>
    public double RelativeSensitivity { get; init; }
}
