// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SimulationRunner: thin wrapper delegating to SimulationPipeline

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Convenience runner that wraps <see cref="SimulationPipeline"/> with a
/// configuration object (<see cref="SimulationOptions"/>) for users who prefer
/// the constructor-based API over the fluent builder.
///
/// For new code, prefer <see cref="SimulationPipeline"/> directly.
/// </summary>
public sealed class SimulationRunner
{
    private readonly SimulationPipeline _pipeline;

    public SimulationRunner(
        Asset asset,
        DemandModel demandModel,
        DamageModel damageModel,
        LossModel lossModel)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _pipeline = new SimulationPipeline(asset)
            .WithDemand(demandModel)
            .WithDamage(damageModel)
            .WithLoss(lossModel);
    }

    public SimulationOptions Options { get; set; } = new();

    /// <summary>Execute the full pipeline for <paramref name="numberOfSimulations"/> realisations.</summary>
    public SimulationResult Run(int numberOfSimulations = 10_000, int seed = -1)
    {
        _pipeline
            .WithSamplingMethod(Options.SamplingMethod)
            .WithDecisionVariables(Options.DecisionVariables);

        return _pipeline.Run(numberOfSimulations, seed);
    }
}

/// <summary>Options for <see cref="SimulationRunner"/>.</summary>
public sealed class SimulationOptions
{
    public SamplingMethod SamplingMethod { get; set; } = SamplingMethod.LatinHypercube;
    public DecisionVariable DecisionVariables { get; set; } = DecisionVariable.All;
}
