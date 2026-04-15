// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SensitivityAnalyzer: local, global, and correlation sensitivity analysis

using System.Collections.Concurrent;
using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Copulas;
using ReSilico.Domain.Enums;

namespace ReSilico.Analysis.Sensitivity;

// ─── Internal parameter descriptor ───────────────────────────────────────────

/// <summary>Identifies which model slot a parameter belongs to.</summary>
internal enum ParameterKind
{
    EdpTheta1,       // EDP log-mean (lognormal) or mean (normal)
    EdpTheta2,       // EDP dispersion (lognormal) or std-dev (normal) — must be > 0
    FragilityMedian, // Fragility function median capacity — must be > 0
    FragilityBeta,   // Fragility function dispersion — must be > 0
    LossMedian,      // Consequence function median loss — must be ≥ 0
    LossBeta,        // Consequence function dispersion — must be ≥ 0
    Correlation,     // Off-diagonal ρ_ij of the EDP correlation matrix — in (-1, 1)
}

/// <summary>
/// Fully describes a single perturbable scalar parameter within a pipeline.
/// Indices are opaque handles into the specific model lists.
/// </summary>
/// <param name="Name">Human-readable label.</param>
/// <param name="Group">Category string: "EDP", "Fragility", "Loss", "Correlation".</param>
/// <param name="BaselineValue">Value at the unperturbed baseline.</param>
/// <param name="Kind">Which slot to perturb.</param>
/// <param name="Idx1">EDP spec index | component (fragility) index | consequence function index | correlation row.</param>
/// <param name="Idx2">Unused for EDP/Loss | limit-state index | correlation column.</param>
/// <param name="Key">Component ID string (used for Loss parameters to key into the dictionary).</param>
internal sealed record ParameterSpec(
    string Name,
    string Group,
    double BaselineValue,
    ParameterKind Kind,
    int Idx1 = 0,
    int Idx2 = 0,
    string Key = "");

// ─── SensitivityAnalyzer ─────────────────────────────────────────────────────

/// <summary>
/// Computes sensitivity indices for a <see cref="SimulationPipeline"/> with respect to
/// all perturbable scalar parameters: EDP distribution parameters, fragility medians and
/// dispersions, consequence function medians and dispersions, and EDP correlation
/// coefficients.
///
/// Three algorithmic modes (set via <see cref="SensitivityOptions.Method"/>):
/// <list type="bullet">
///   <item>
///     <term><see cref="SensitivityMethod.Local"/></term>
///     <description>
///       Centered finite-difference derivative ∂F/∂θ.  Two extra pipeline runs per
///       parameter (θ± = θ·(1±ε)).  Fast and exact for smooth response surfaces.
///     </description>
///   </item>
///   <item>
///     <term><see cref="SensitivityMethod.Global"/></term>
///     <description>
///       Morris elementary-effects sweep.  Each parameter is evaluated at
///       <see cref="SensitivityOptions.GlobalLevels"/> levels spanning
///       [θ(1−2ε), θ(1+2ε)].  Reports μ* (mean absolute elementary effect) in
///       <see cref="SensitivityEntry.SensitivityValue"/> and an approximate first-order
///       Sobol index in <see cref="SensitivityEntry.RelativeSensitivity"/>.
///     </description>
///   </item>
///   <item>
///     <term><see cref="SensitivityMethod.Correlation"/></term>
///     <description>
///       Perturbs off-diagonal entries of the EDP correlation matrix one pair at a time
///       and measures the change in the target tail metric.
///       Requires a correlation matrix or copula on the demand model.
///     </description>
///   </item>
/// </list>
///
/// Safety guarantees:
///   • The original pipeline is never mutated; all perturbations create fresh objects.
///   • Fragility and consequence function instances are reused (not copied) when not
///     being perturbed — they are effectively immutable.
///   • Parameter loops are parallelised via <see cref="Parallel.ForEach{T}"/>.
///     Each iteration creates its own pipeline objects so no shared mutable state is accessed.
/// </summary>
public sealed class SensitivityAnalyzer
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Execute a full sensitivity analysis and return ranked results.
    /// </summary>
    /// <param name="pipeline">
    /// A fully configured <see cref="SimulationPipeline"/>
    /// (demand, damage, and loss models must all be wired).
    /// </param>
    /// <param name="options">Sensitivity configuration.</param>
    /// <returns>
    /// A <see cref="SensitivityResult"/> with one entry per perturbable parameter.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// If the pipeline is missing any required model, or if
    /// <see cref="SensitivityMethod.Correlation"/> is requested but no correlation
    /// structure is configured on the demand model.
    /// </exception>
    public SensitivityResult Analyze(SimulationPipeline pipeline, SensitivityOptions options)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(options);
        ValidatePipeline(pipeline);
        ValidateOptions(options);

        return options.Method switch
        {
            SensitivityMethod.Local       => AnalyzeLocal(pipeline, options),
            SensitivityMethod.Global      => AnalyzeGlobal(pipeline, options),
            SensitivityMethod.Correlation => AnalyzeCorrelation(pipeline, options),
            _ => throw new ArgumentOutOfRangeException(nameof(options),
                     $"Unknown SensitivityMethod: {options.Method}"),
        };
    }

    // ── Local (centered finite-difference) ───────────────────────────────────

    private static SensitivityResult AnalyzeLocal(
        SimulationPipeline pipeline, SensitivityOptions options)
    {
        double baseline = RunAndExtract(pipeline, options, options.Seed);
        double ε        = options.PerturbationFactor;

        var parameters = EnumeratePhysicalParameters(pipeline);
        var entries    = new ConcurrentBag<SensitivityEntry>();

        Parallel.ForEach(parameters, param =>
        {
            double θ = param.BaselineValue;
            // Skip zero-baseline: multiplicative perturbation collapses to zero.
            if (θ == 0.0) return;

            // For negative θ (e.g., lognormal log-mean), θ×(1+ε) < θ < θ×(1-ε).
            // The FD formula (f+ - f-) / (2εθ) remains algebraically correct
            // because both numerator and denominator flip sign together.
            double θPlus  = θ * (1.0 + ε);
            double θMinus = θ * (1.0 - ε);

            if (IsInvalidParameterValue(param.Kind, θPlus) ||
                IsInvalidParameterValue(param.Kind, θMinus)) return;

            double fPlus  = RunAndExtract(
                CreatePerturbedPipeline(pipeline, param, θPlus),  options, options.Seed);
            double fMinus = RunAndExtract(
                CreatePerturbedPipeline(pipeline, param, θMinus), options, options.Seed);

            // Centered finite difference: S = (f+ - f-) / (2εθ)
            double sensitivity = (fPlus - fMinus) / (2.0 * ε * θ);

            // Elasticity: (ΔF/F) / (2ε) — normalised, dimensionless
            double relSensitivity = baseline > 0.0
                ? (fPlus - fMinus) / (2.0 * ε * baseline)
                : double.NaN;

            entries.Add(new SensitivityEntry
            {
                Parameter           = param.Name,
                ParameterGroup      = param.Group,
                SensitivityValue    = sensitivity,
                BaselineValue       = baseline,
                PerturbedPlusValue  = fPlus,
                PerturbedMinusValue = fMinus,
                RelativeSensitivity = relSensitivity,
            });
        });

        return new SensitivityResult
        {
            Entries         = [.. entries],
            BaselineValue   = baseline,
            TargetStatistic = FormatStatistic(options),
        };
    }

    // ── Global (Morris elementary-effects sweep) ──────────────────────────────

    /// <summary>
    /// For each parameter, sweep L evenly-spaced levels spanning [θ(1−2ε), θ(1+2ε)].
    /// Reports Morris μ* in <see cref="SensitivityEntry.SensitivityValue"/> and
    /// the approximate first-order Sobol index in <see cref="SensitivityEntry.RelativeSensitivity"/>.
    /// </summary>
    private static SensitivityResult AnalyzeGlobal(
        SimulationPipeline pipeline, SensitivityOptions options)
    {
        double baseline = RunAndExtract(pipeline, options, options.Seed);
        double ε        = options.PerturbationFactor;
        int    L        = Math.Max(options.GlobalLevels, 3);

        var parameters = EnumeratePhysicalParameters(pipeline);
        int n = parameters.Count;

        // Arrays indexed by parameter index — written once per c, read after barrier.
        var allVariances = new double[n];
        var allEntries   = new SensitivityEntry?[n];

        Parallel.For(0, n, c =>
        {
            var param = parameters[c];
            double θ = param.BaselineValue;
            if (θ == 0.0) return;

            // L evenly-spaced levels: [θ(1-2ε) … θ(1+2ε)]
            double low  = θ * (1.0 - 2.0 * ε);
            double high = θ * (1.0 + 2.0 * ε);

            // For negative θ: low > high algebraically — swap to preserve
            // ascending order so EE computation makes directional sense.
            if (low > high) (low, high) = (high, low);

            double[] levels  = new double[L];
            double[] outputs = new double[L];

            for (int l = 0; l < L; l++)
                levels[l] = low + (high - low) * l / (L - 1);

            for (int l = 0; l < L; l++)
            {
                double lvl = levels[l];
                if (IsInvalidParameterValue(param.Kind, lvl))
                    { outputs[l] = baseline; continue; }

                // Offset seed by level to avoid aliasing between levels
                var pertPipeline = CreatePerturbedPipeline(pipeline, param, lvl);
                outputs[l] = RunAndExtract(pertPipeline, options, options.Seed + l + 1);
            }

            // Morris elementary effects between adjacent levels
            double[] ees = new double[L - 1];
            for (int l = 0; l < L - 1; l++)
            {
                double Δθ = levels[l + 1] - levels[l];
                ees[l] = Δθ != 0.0 ? (outputs[l + 1] - outputs[l]) / Δθ : 0.0;
            }

            double muStar    = ees.Average(e => Math.Abs(e));
            double meanOut   = outputs.Average();
            double varOut    = outputs.Average(y => (y - meanOut) * (y - meanOut));

            allVariances[c] = varOut;
            allEntries[c]   = new SensitivityEntry
            {
                Parameter           = param.Name,
                ParameterGroup      = param.Group,
                SensitivityValue    = muStar,
                BaselineValue       = baseline,
                PerturbedPlusValue  = outputs[L - 1],
                PerturbedMinusValue = outputs[0],
                RelativeSensitivity = varOut,  // replaced below with Sobol approximation
            };
        });

        // Normalise to approximate first-order Sobol indices: S_i ≈ Var_i / Var_total.
        // Rebuild each entry with the final RelativeSensitivity value.
        double totalVariance = allVariances.Sum();
        var finalEntries = new List<SensitivityEntry>(n);
        for (int c = 0; c < n; c++)
        {
            if (allEntries[c] is null) continue;
            var e     = allEntries[c]!;
            double si = totalVariance > 0.0 ? allVariances[c] / totalVariance : 0.0;
            finalEntries.Add(new SensitivityEntry
            {
                Parameter           = e.Parameter,
                ParameterGroup      = e.ParameterGroup,
                SensitivityValue    = e.SensitivityValue,
                BaselineValue       = e.BaselineValue,
                PerturbedPlusValue  = e.PerturbedPlusValue,
                PerturbedMinusValue = e.PerturbedMinusValue,
                RelativeSensitivity = si,
            });
        }

        return new SensitivityResult
        {
            Entries         = finalEntries,
            BaselineValue   = baseline,
            TargetStatistic = FormatStatistic(options),
        };
    }

    // ── Correlation (per-pair EDP correlation perturbation) ───────────────────

    /// <summary>
    /// Perturbs each off-diagonal correlation coefficient ρ_ij by ±ε (absolute delta)
    /// and measures the change in the target tail metric.
    /// A positive sensitivity means higher correlation → higher metric (typical for CVaR).
    /// </summary>
    private static SensitivityResult AnalyzeCorrelation(
        SimulationPipeline pipeline, SensitivityOptions options)
    {
        var corrParams = EnumerateCorrelationParameters(pipeline);
        if (corrParams.Count == 0)
            throw new InvalidOperationException(
                "Correlation sensitivity requires a correlation matrix or copula on the " +
                "demand model.  Call DemandModel.SetCorrelation() or SetCopula() first.");

        double baseline = RunAndExtract(pipeline, options, options.Seed);
        double ε        = options.PerturbationFactor; // absolute delta for ρ

        var entries = new ConcurrentBag<SensitivityEntry>();

        Parallel.ForEach(corrParams, param =>
        {
            double ρ      = param.BaselineValue;
            double ρPlus  = Math.Clamp(ρ + ε, -0.999, 0.999);
            double ρMinus = Math.Clamp(ρ - ε, -0.999, 0.999);
            double actual2Δ = ρPlus - ρMinus;

            double fPlus  = RunAndExtract(
                CreatePerturbedPipeline(pipeline, param, ρPlus),  options, options.Seed);
            double fMinus = RunAndExtract(
                CreatePerturbedPipeline(pipeline, param, ρMinus), options, options.Seed);

            // Finite difference: S = (f+ - f-) / (ρ+ - ρ-)
            double sensitivity    = actual2Δ != 0.0 ? (fPlus - fMinus) / actual2Δ : 0.0;
            double relSensitivity = baseline > 0.0 ? (fPlus - fMinus) / baseline : double.NaN;

            entries.Add(new SensitivityEntry
            {
                Parameter           = param.Name,
                ParameterGroup      = param.Group,
                SensitivityValue    = sensitivity,
                BaselineValue       = baseline,
                PerturbedPlusValue  = fPlus,
                PerturbedMinusValue = fMinus,
                RelativeSensitivity = relSensitivity,
            });
        });

        return new SensitivityResult
        {
            Entries         = [.. entries],
            BaselineValue   = baseline,
            TargetStatistic = FormatStatistic(options),
        };
    }

    // ── Metric extraction ─────────────────────────────────────────────────────

    private static double RunAndExtract(
        SimulationPipeline pipeline, SensitivityOptions options, int seed)
    {
        SimulationResult result = pipeline.Run(options.SampleSize, seed);
        return ExtractMetric(result, options);
    }

    private static double ExtractMetric(SimulationResult result, SensitivityOptions options)
    {
        // Clamp quantile away from boundaries to satisfy SimulationResult's pre-conditions.
        double q = Math.Clamp(options.TargetQuantile, 1e-6, 1.0 - 1e-6);

        return options.TargetStatistic switch
        {
            SensitivityStatistic.Mean       => result.MeanCost,
            SensitivityStatistic.StdDev     => result.StdDevCost,
            SensitivityStatistic.Percentile => result.CostAtPercentile(q),
            SensitivityStatistic.CVaR       => result.CostCVaR(q),
            _ => throw new ArgumentOutOfRangeException(nameof(options.TargetStatistic),
                     $"Unsupported statistic: {options.TargetStatistic}"),
        };
    }

    private static string FormatStatistic(SensitivityOptions options) =>
        options.TargetStatistic switch
        {
            SensitivityStatistic.Percentile =>
                $"Percentile(q={options.TargetQuantile:P0})",
            SensitivityStatistic.CVaR =>
                $"CVaR(q={options.TargetQuantile:P0})",
            _ => options.TargetStatistic.ToString(),
        };

    // ── Parameter enumeration ─────────────────────────────────────────────────

    /// <summary>
    /// Enumerate all EDP, fragility, and loss parameters in the pipeline.
    /// Correlation parameters are handled separately via
    /// <see cref="EnumerateCorrelationParameters"/>.
    /// </summary>
    private static List<ParameterSpec> EnumeratePhysicalParameters(
        SimulationPipeline pipeline)
    {
        var result = new List<ParameterSpec>();

        // ── 1. EDP parameters ─────────────────────────────────────────────────
        var edpSpecs = pipeline.DemandModel!.Specs;
        for (int i = 0; i < edpSpecs.Count; i++)
        {
            var spec = edpSpecs[i];
            string key = spec.Edp.Key;

            // Theta1 may be any real (including negative for ln-space);
            // skip only if exactly 0 (multiplicative perturbation degenerates).
            result.Add(new ParameterSpec(
                $"EDP[{key}].Theta1", "EDP",
                spec.Theta1, ParameterKind.EdpTheta1, Idx1: i));

            if (spec.Theta2 > 0.0)
                result.Add(new ParameterSpec(
                    $"EDP[{key}].Theta2", "EDP",
                    spec.Theta2, ParameterKind.EdpTheta2, Idx1: i));
        }

        // ── 2. Fragility parameters ───────────────────────────────────────────
        var damageSpecs = pipeline.DamageModel!.Specs;
        for (int c = 0; c < damageSpecs.Count; c++)
        {
            var compSpec = damageSpecs[c];
            string cId   = compSpec.Component.Id;
            for (int k = 0; k < compSpec.FragilityFunctions.Count; k++)
            {
                var ff = compSpec.FragilityFunctions[k];
                result.Add(new ParameterSpec(
                    $"Fragility[{cId}][LS{k + 1}].Median", "Fragility",
                    ff.Median, ParameterKind.FragilityMedian, Idx1: c, Idx2: k));
                result.Add(new ParameterSpec(
                    $"Fragility[{cId}][LS{k + 1}].Beta", "Fragility",
                    ff.Beta, ParameterKind.FragilityBeta, Idx1: c, Idx2: k));
            }
        }

        // ── 3. Loss / consequence parameters ─────────────────────────────────
        foreach (var (compId, fns) in pipeline.LossModel!.Consequences)
        {
            for (int j = 0; j < fns.Count; j++)
            {
                var cf = fns[j];
                if (cf.MedianLoss > 0.0)
                    result.Add(new ParameterSpec(
                        $"Loss[{compId}][DS{cf.DamageStateIndex}].Median", "Loss",
                        cf.MedianLoss, ParameterKind.LossMedian,
                        Idx1: j, Key: compId));
                if (cf.Beta > 0.0)
                    result.Add(new ParameterSpec(
                        $"Loss[{compId}][DS{cf.DamageStateIndex}].Beta", "Loss",
                        cf.Beta, ParameterKind.LossBeta,
                        Idx1: j, Key: compId));
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerate all unique off-diagonal pairs (i, j) with i &lt; j from the EDP
    /// correlation matrix, or from the copula's internal matrix.
    /// </summary>
    private static List<ParameterSpec> EnumerateCorrelationParameters(
        SimulationPipeline pipeline)
    {
        var result = new List<ParameterSpec>();
        var demand  = pipeline.DemandModel!;

        // Prefer explicit correlation matrix; fall back to copula's internal matrix.
        double[,]? rho =
            demand.CorrelationMatrix ??
            (demand.ActiveCopula is CopulaBase cb ? cb.CorrelationMatrix : null);

        if (rho is null) return result;

        int n = rho.GetLength(0);
        var keys = demand.Specs.Select(s => s.Edp.Key).ToList();

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                string ki = i < keys.Count ? keys[i] : i.ToString();
                string kj = j < keys.Count ? keys[j] : j.ToString();
                result.Add(new ParameterSpec(
                    $"Corr[{ki},{kj}]", "Correlation",
                    rho[i, j], ParameterKind.Correlation,
                    Idx1: i, Idx2: j));
            }

        return result;
    }

    // ── Pipeline cloning ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a new <see cref="SimulationPipeline"/> identical to <paramref name="source"/>
    /// except that <paramref name="param"/> is set to <paramref name="perturbedValue"/>.
    /// The original pipeline and all its sub-models are left unmodified.
    /// </summary>
    private static SimulationPipeline CreatePerturbedPipeline(
        SimulationPipeline source, ParameterSpec param, double perturbedValue)
    {
        DemandModel demand = CloneDemandModel(source.DemandModel!, param, perturbedValue);
        DamageModel damage = CloneDamageModel(source.DamageModel!, param, perturbedValue);
        LossModel   loss   = CloneLossModel  (source.LossModel!,   param, perturbedValue);

        // The Asset (quantities, component metadata) is shared by reference — it is never
        // mutated by the pipeline or by any model.
        return new SimulationPipeline(source.Asset)
            .WithDemand(demand)
            .WithDamage(damage)
            .WithLoss(loss);
    }

    /// <summary>
    /// Clone the demand model, optionally perturbing one EDP parameter or the
    /// correlation structure (when <paramref name="param.Kind"/> is
    /// <see cref="ParameterKind.Correlation"/>).
    /// </summary>
    private static DemandModel CloneDemandModel(
        DemandModel source, ParameterSpec param, double perturbedValue)
    {
        var dm = new DemandModel();

        // Clone EDP specs — perturb Theta1/Theta2 if this is the target parameter.
        for (int i = 0; i < source.Specs.Count; i++)
        {
            var spec = source.Specs[i];
            double t1 = spec.Theta1;
            double t2 = spec.Theta2;

            if (param.Kind == ParameterKind.EdpTheta1 && param.Idx1 == i)
                t1 = perturbedValue;
            else if (param.Kind == ParameterKind.EdpTheta2 && param.Idx1 == i)
                t2 = perturbedValue;

            dm.AddEdp(new EdpDistributionSpec(
                spec.Edp, spec.Kind, t1, t2, spec.TruncLower, spec.TruncUpper));
        }

        // Clone correlation structure — perturb ρ_ij if this is the target pair.
        if (source.ActiveCopula is TCopula tc)
        {
            double[,] rho = (double[,])tc.CorrelationMatrix.Clone();
            if (param.Kind == ParameterKind.Correlation)
            {
                rho[param.Idx1, param.Idx2] = perturbedValue;
                rho[param.Idx2, param.Idx1] = perturbedValue;
            }
            // Use a fixed seed for the cloned copula so chi-squared mixing is
            // consistent across baseline and all perturbed runs.
            dm.SetCopula(new TCopula(tc.DegreesOfFreedom, rho, seed: 42));
        }
        else if (source.ActiveCopula is CopulaBase cbOther)
        {
            // GaussianCopula or any other CopulaBase subclass
            double[,] rho = (double[,])cbOther.CorrelationMatrix.Clone();
            if (param.Kind == ParameterKind.Correlation)
            {
                rho[param.Idx1, param.Idx2] = perturbedValue;
                rho[param.Idx2, param.Idx1] = perturbedValue;
            }
            dm.SetCopula(new GaussianCopula(rho));
        }
        else if (source.CorrelationMatrix is not null)
        {
            double[,] rho = (double[,])source.CorrelationMatrix.Clone();
            if (param.Kind == ParameterKind.Correlation)
            {
                rho[param.Idx1, param.Idx2] = perturbedValue;
                rho[param.Idx2, param.Idx1] = perturbedValue;
            }
            dm.SetCorrelation(rho);
        }
        // If no correlation was configured on source, none is set on the clone.

        return dm;
    }

    /// <summary>
    /// Clone the damage model, optionally perturbing one fragility function's
    /// Median or Beta.  All other fragility functions are reused by reference
    /// (they are effectively immutable).
    /// </summary>
    private static DamageModel CloneDamageModel(
        DamageModel source, ParameterSpec param, double perturbedValue)
    {
        var dm    = new DamageModel();
        var specs = source.Specs;

        for (int c = 0; c < specs.Count; c++)
        {
            var origSpec = specs[c];
            var newSpec  = new ComponentFragilitySpec(origSpec.Component);
            var ffs      = origSpec.FragilityFunctions;

            for (int k = 0; k < ffs.Count; k++)
            {
                var ff = ffs[k];

                if (param.Kind == ParameterKind.FragilityMedian
                    && param.Idx1 == c && param.Idx2 == k)
                {
                    newSpec.AddFragilityFunction(
                        new FragilityFunction(perturbedValue, ff.Beta, ff.LimitStateLabel));
                }
                else if (param.Kind == ParameterKind.FragilityBeta
                         && param.Idx1 == c && param.Idx2 == k)
                {
                    newSpec.AddFragilityFunction(
                        new FragilityFunction(ff.Median, perturbedValue, ff.LimitStateLabel));
                }
                else
                {
                    newSpec.AddFragilityFunction(ff); // reuse — immutable
                }
            }

            dm.Add(newSpec);
        }

        return dm;
    }

    /// <summary>
    /// Clone the loss model, optionally perturbing one consequence function's
    /// MedianLoss or Beta.  All other consequence functions are reused by
    /// reference (they are effectively immutable after construction).
    /// </summary>
    private static LossModel CloneLossModel(
        LossModel source, ParameterSpec param, double perturbedValue)
    {
        var lm = new LossModel();

        foreach (var (compId, fns) in source.Consequences)
        {
            for (int j = 0; j < fns.Count; j++)
            {
                var cf = fns[j];

                if (param.Kind == ParameterKind.LossMedian
                    && param.Key == compId && param.Idx1 == j)
                {
                    lm.AddConsequence(compId, new ConsequenceFunction(
                        cf.DamageStateIndex, cf.DecisionVariable,
                        Math.Max(perturbedValue, 0.0), cf.Beta));
                }
                else if (param.Kind == ParameterKind.LossBeta
                         && param.Key == compId && param.Idx1 == j)
                {
                    lm.AddConsequence(compId, new ConsequenceFunction(
                        cf.DamageStateIndex, cf.DecisionVariable,
                        cf.MedianLoss, Math.Max(perturbedValue, 0.0)));
                }
                else
                {
                    lm.AddConsequence(compId, cf); // reuse — immutable
                }
            }
        }

        return lm;
    }

    // ── Validity helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Return true when a perturbed value would violate a constructor pre-condition
    /// and the parameter should be skipped rather than producing an exception.
    /// </summary>
    private static bool IsInvalidParameterValue(ParameterKind kind, double value) =>
        kind switch
        {
            ParameterKind.FragilityMedian => value <= 0.0,
            ParameterKind.FragilityBeta   => value <= 0.0,
            ParameterKind.LossMedian      => value < 0.0,
            ParameterKind.LossBeta        => value < 0.0,
            ParameterKind.EdpTheta2       => value <= 0.0,
            _ => false, // EdpTheta1 accepts any real; Correlation clamped externally
        };

    private static void ValidatePipeline(SimulationPipeline pipeline)
    {
        if (pipeline.DemandModel is null)
            throw new InvalidOperationException(
                "SensitivityAnalyzer requires a demand model — call pipeline.WithDemand() first.");
        if (pipeline.DamageModel is null)
            throw new InvalidOperationException(
                "SensitivityAnalyzer requires a damage model — call pipeline.WithDamage() first.");
        if (pipeline.LossModel is null)
            throw new InvalidOperationException(
                "SensitivityAnalyzer requires a loss model — call pipeline.WithLoss() first.");
    }

    private static void ValidateOptions(SensitivityOptions options)
    {
        if (options.SampleSize < 10)
            throw new ArgumentOutOfRangeException(nameof(options),
                "SampleSize must be ≥ 10.");
        if (options.PerturbationFactor <= 0.0 || options.PerturbationFactor >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(options),
                "PerturbationFactor must be in (0, 1).");
    }
}
