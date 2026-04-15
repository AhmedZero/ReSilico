// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SimulationPipeline: fluent builder for the EDP → Damage → Loss pipeline

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using System.Collections.Generic;

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Fluent builder that assembles and executes the full probabilistic pipeline:
///
///   Asset  →  DemandModel  →  DamageModel  →  LossModel  →  SimulationResult
///
/// Example usage:
/// <code>
/// SimulationResult result = new SimulationPipeline(asset)
///     .WithDemand(demandModel)
///     .WithDamage(damageModel)
///     .WithLoss(lossModel)
///     .WithSampler(LatinHypercubeSampler.Standard)
///     .WithDecisionVariables(DecisionVariable.Cost | DecisionVariable.Time)
///     .Run(numberOfSimulations: 10_000, seed: 42);
/// </code>
///
/// Design notes:
///   • Each <c>With*</c> call returns <c>this</c> (fluent interface).
///   • <see cref="Run"/> is idempotent — safe to call multiple times with
///     different counts or seeds.
///   • The pipeline owns no persistent state between runs; it is just a
///     wiring layer.
/// </summary>
public sealed class SimulationPipeline
{
    private readonly Asset _asset;
    private DemandModel? _demandModel;
    private DamageModel? _damageModel;
    private LossModel? _lossModel;
    private ISampler _sampler = LatinHypercubeSampler.Standard;
    private DecisionVariable _decisionVariables = DecisionVariable.All;

    public SimulationPipeline(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _asset = asset;
    }

    // ── Read-only introspection ───────────────────────────────────────────────

    /// <summary>The asset this pipeline was constructed for.</summary>
    public Asset Asset => _asset;

    /// <summary>
    /// The demand model wired via <see cref="WithDemand"/>,
    /// or <see langword="null"/> if not yet configured.
    /// </summary>
    public DemandModel? DemandModel => _demandModel;

    /// <summary>
    /// The damage model wired via <see cref="WithDamage"/>,
    /// or <see langword="null"/> if not yet configured.
    /// </summary>
    public DamageModel? DamageModel => _damageModel;

    /// <summary>
    /// The loss model wired via <see cref="WithLoss"/>,
    /// or <see langword="null"/> if not yet configured.
    /// </summary>
    public LossModel? LossModel => _lossModel;

    // ── Fluent configuration ──────────────────────────────────────────────────

    /// <summary>Supply the demand model (EDP distributions and correlation).</summary>
    public SimulationPipeline WithDemand(DemandModel demandModel)
    {
        _demandModel = demandModel ?? throw new ArgumentNullException(nameof(demandModel));
        return this;
    }

    /// <summary>Supply the damage model (component fragility specifications).</summary>
    public SimulationPipeline WithDamage(DamageModel damageModel)
    {
        _damageModel = damageModel ?? throw new ArgumentNullException(nameof(damageModel));
        return this;
    }

    /// <summary>Supply the loss model (consequence functions).</summary>
    public SimulationPipeline WithLoss(LossModel lossModel)
    {
        _lossModel = lossModel ?? throw new ArgumentNullException(nameof(lossModel));
        return this;
    }

    /// <summary>
    /// Override the sampling strategy (defaults to
    /// <see cref="LatinHypercubeSampler.Standard"/>).
    /// </summary>
    public SimulationPipeline WithSampler(ISampler sampler)
    {
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        return this;
    }

    /// <summary>Set the sampling method via the <see cref="SamplingMethod"/> enum.</summary>
    public SimulationPipeline WithSamplingMethod(SamplingMethod method)
    {
        _sampler = SamplerFactory.Create(method);
        return this;
    }

    /// <summary>Specify which decision variables to compute (default: All).</summary>
    public SimulationPipeline WithDecisionVariables(DecisionVariable dvs)
    {
        _decisionVariables = dvs;
        return this;
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Execute the full EDP → Damage → Loss pipeline for
    /// <paramref name="numberOfSimulations"/> Monte Carlo realisations.
    ///
    /// Seeds are derived from the master <paramref name="seed"/>:
    ///   demand seed = seed, damage seed = seed+1, loss seed = seed+2.
    /// This guarantees independence between the three sampling stages while
    /// remaining deterministic given the master seed.
    /// </summary>
    public SimulationResult Run(int numberOfSimulations = 10_000, int seed = -1)
    {
        if (_demandModel is null)
            throw new InvalidOperationException("WithDemand() must be called before Run().");
        if (_damageModel is null)
            throw new InvalidOperationException("WithDamage() must be called before Run().");
        if (_lossModel is null)
            throw new InvalidOperationException("WithLoss() must be called before Run().");

        ArgumentOutOfRangeException.ThrowIfLessThan(numberOfSimulations, 10, nameof(numberOfSimulations));

        int demandSeed = seed;
        int damageSeed = seed >= 0 ? seed + 1 : -1;
        int lossSeed   = seed >= 0 ? seed + 2 : -1;

        // ── Step 1: Demand ────────────────────────────────────────────────────
        _demandModel.GenerateSample(numberOfSimulations, demandSeed, _sampler);

        // ── Step 2: Damage ────────────────────────────────────────────────────
        DamageSample damageSample = _damageModel.Evaluate(
            edpKey => _demandModel.GetEdpSample(edpKey),
            numberOfSimulations,
            damageSeed);

        // ── Step 3: Loss ──────────────────────────────────────────────────────
        var quantities = _asset.Components
            .ToDictionary(c => c.Id, c => c.Quantity);

        LossSample lossSample = _lossModel.Calculate(
            damageSample,
            quantities,
            _decisionVariables,
            lossSeed);

        return new SimulationResult(lossSample, damageSample, numberOfSimulations);
    }

    // ── Adaptive Monte Carlo ──────────────────────────────────────────────────

    /// <summary>
    /// Execute the pipeline adaptively, adding batches until convergence criteria
    /// are met or <see cref="AdaptiveOptions.MaxSamples"/> is exhausted.
    ///
    /// Convergence criteria (both required after ≥ 2 iterations):
    ///   1. Relative standard error of the mean &lt; <see cref="AdaptiveOptions.ToleranceMean"/>.
    ///   2. Iteration-to-iteration relative change of CVaR &lt; <see cref="AdaptiveOptions.ToleranceTail"/>.
    ///
    /// Seed derivation:
    ///   Batch <c>i</c> uses master seed <c>options.Seed + i * 3</c>, which maps to
    ///   demand/damage/loss sub-seeds <c>+0/+1/+2</c> — ensuring full independence
    ///   between batches while remaining fully reproducible.
    /// </summary>
    /// <param name="options">
    /// Adaptive configuration.  Pass <c>null</c> to use default settings.
    /// </param>
    public AdaptiveSimulationResult RunAdaptive(AdaptiveOptions? options = null)
    {
        options ??= new AdaptiveOptions();

        if (_demandModel is null)
            throw new InvalidOperationException("WithDemand() must be called before RunAdaptive().");
        if (_damageModel is null)
            throw new InvalidOperationException("WithDamage() must be called before RunAdaptive().");
        if (_lossModel is null)
            throw new InvalidOperationException("WithLoss() must be called before RunAdaptive().");
        if (options.InitialSamples < 10)
            throw new ArgumentOutOfRangeException(nameof(options), "InitialSamples must be ≥ 10.");
        if (options.BatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be ≥ 1.");
        if (options.MaxSamples < options.InitialSamples)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSamples must be ≥ InitialSamples.");

        var stats       = new IncrementalStats();
        var diagnostics = new List<ConvergenceDiagnostic>();
        var batchLoss   = new List<LossSample>();
        var batchDamage = new List<DamageSample>();

        // Running accumulator for exact percentile / CVaR computation.
        // Memory: O(MaxSamples × 8 bytes) — at most ~1.6 MB for 200 k samples.
        var allCosts = new List<double>(options.MaxSamples);

        bool   converged = false;
        int    iteration = 0;
        double prevCVaR  = double.NaN;

        while (true)
        {
            // ── Determine batch size ──────────────────────────────────────
            int batchN    = iteration == 0 ? options.InitialSamples : options.BatchSize;
            int remaining = options.MaxSamples - allCosts.Count;
            if (remaining <= 0) break;
            batchN = Math.Min(batchN, remaining);

            // Offset by 3 per iteration so demand/damage/loss sub-seeds don't collide.
            int batchSeed = options.Seed >= 0 ? options.Seed + iteration * 3 : -1;

            SimulationResult batch = Run(batchN, batchSeed);
            batchLoss  .Add(batch.LossSample);
            batchDamage.Add(batch.DamageSample);

            // ── Update incremental statistics ─────────────────────────────
            foreach (double c in batch.LossSample.TotalCosts)
            {
                allCosts.Add(c);
                stats.Add(c);
            }

            iteration++;

            // ── Compute tail metrics on the full accumulated sample ───────
            // Sorting O(N log N) per iteration is acceptable: N ≤ 200 k.
            double[] sorted = [.. allCosts];
            Array.Sort(sorted);

            double percentile    = AdaptivePercentile(sorted, options.TargetQuantile);
            double cvar          = AdaptiveCVaR(sorted, options.TargetQuantile);
            double tailRelChange = double.IsNaN(prevCVaR) || prevCVaR == 0.0
                                       ? double.PositiveInfinity
                                       : Math.Abs(cvar - prevCVaR) / Math.Abs(prevCVaR);

            diagnostics.Add(new ConvergenceDiagnostic(
                Iteration:          iteration,
                TotalSamples:       allCosts.Count,
                MeanCost:           stats.Mean,
                StdDevCost:         stats.StdDev,
                PercentileCost:     percentile,
                CVaR:               cvar,
                MeanRelativeError:  stats.RelativeMeanError,
                TailRelativeChange: tailRelChange));

            // ── Check convergence (requires ≥ 2 iterations) ──────────────
            if (iteration >= 2
                && stats.RelativeMeanError < options.ToleranceMean
                && tailRelChange           < options.ToleranceTail)
            {
                converged = true;
                break;
            }

            prevCVaR = cvar;
        }

        // ── Build merged samples ──────────────────────────────────────────────
        LossSample   mergedLoss   = MergeLossSamples(batchLoss);
        DamageSample mergedDamage = MergeDamageSamples(batchDamage);

        return new AdaptiveSimulationResult(
            mergedLoss, mergedDamage, allCosts.Count,
            converged, iteration, diagnostics);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Empirical p-quantile from a pre-sorted ascending array.
    /// Uses nearest-rank convention: index = floor(p × n), clamped to [0, n-1].
    /// </summary>
    private static double AdaptivePercentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0.0;
        int idx = Math.Clamp((int)(p * sorted.Length), 0, sorted.Length - 1);
        return sorted[idx];
    }

    /// <summary>
    /// CVaR (Expected Shortfall) at level p: mean of all values ≥ the p-quantile.
    /// The p-quantile threshold is included (i.e., CVaR ≥ VaR at level p).
    /// </summary>
    private static double AdaptiveCVaR(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0.0;
        int start = Math.Clamp((int)(p * sorted.Length), 0, sorted.Length - 1);
        double sum = 0.0;
        int count = sorted.Length - start;
        for (int i = start; i < sorted.Length; i++) sum += sorted[i];
        return count == 0 ? sorted[^1] : sum / count;
    }

    /// <summary>Concatenate multiple <see cref="LossSample"/> objects row-by-row.</summary>
    private static LossSample MergeLossSamples(List<LossSample> batches)
    {
        int total  = batches.Sum(b => b.Count);
        var merged = new LossSample(batches[0].ComponentIds, total);
        int offset = 0;

        foreach (var b in batches)
        {
            int n  = b.Count;
            int nc = b.ComponentIds.Count;

            Array.Copy(b.TotalCosts, 0, merged.TotalCosts, offset, n);
            Array.Copy(b.TotalTimes, 0, merged.TotalTimes, offset, n);
            Array.Copy(b.Fatalities, 0, merged.Fatalities, offset, n);
            Array.Copy(b.Injuries,   0, merged.Injuries,   offset, n);

            for (int i = 0; i < n; i++)
                for (int c = 0; c < nc; c++)
                    merged.ComponentCosts[offset + i, c] = b.ComponentCosts[i, c];

            offset += n;
        }
        return merged;
    }

    /// <summary>Concatenate multiple <see cref="DamageSample"/> objects row-by-row.</summary>
    private static DamageSample MergeDamageSamples(List<DamageSample> batches)
    {
        int total  = batches.Sum(b => b.Count);
        var merged = new DamageSample(batches[0].ComponentIds, total);
        int offset = 0;

        foreach (var b in batches)
        {
            int n  = b.Count;
            int nc = b.NumberOfComponents;

            for (int i = 0; i < n; i++)
                for (int c = 0; c < nc; c++)
                    merged.DamageStates[offset + i, c] = b.DamageStates[i, c];

            offset += n;
        }
        return merged;
    }
}
