// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SimulationPipeline: fluent builder for the EDP → Damage → Loss pipeline

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;

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
}
