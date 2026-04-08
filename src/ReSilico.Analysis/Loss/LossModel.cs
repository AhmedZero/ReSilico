// ReSilico – Probabilistic Damage and Loss Assessment Engine
// LossModel: damage state → loss consequences via consequence functions

using System.Buffers;
using ReSilico.Analysis.Damage;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;

namespace ReSilico.Analysis.Loss;

/// <summary>
/// Maps a <see cref="DamageSample"/> to realised loss values for each
/// decision variable and each Monte Carlo realisation.
///
/// Per realisation per component:
///   1. Look up the realised DS index.
///   2. Find the <see cref="ConsequenceFunction"/> for (component, DS, DV).
///   3. Sample a loss value (lognormal or deterministic) × component quantity.
///   4. Sum across components per DV.
///
/// Performance:
///   • Parallel.For with thread-local accumulator avoids contention.
///   • Capacity uniform samples are pre-generated once per call.
///   • <see cref="ArrayPool{T}"/> used for temporary per-component buffers.
/// </summary>
public sealed class LossModel
{
    private readonly Dictionary<string, List<ConsequenceFunction>> _consequences = [];

    // ─── Configuration ────────────────────────────────────────────────────────

    public void AddConsequence(string componentId, ConsequenceFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(function);
        if (!_consequences.TryGetValue(componentId, out var list))
            _consequences[componentId] = list = [];
        list.Add(function);
    }

    public void AddConsequences(string componentId, IEnumerable<ConsequenceFunction> functions)
    {
        foreach (var fn in functions) AddConsequence(componentId, fn);
    }

    // ─── Loss calculation ─────────────────────────────────────────────────────

    /// <summary>
    /// Compute loss realisations for all components across all simulations.
    /// </summary>
    /// <param name="damageSample">Damage state assignments from DamageModel.</param>
    /// <param name="componentQuantities">Component ID → quantity (default 1.0).</param>
    /// <param name="decisionVariables">Which decision variables to compute.</param>
    /// <param name="seed">Consequence RNG seed (−1 = clock-based).</param>
    public LossSample Calculate(
        DamageSample damageSample,
        IReadOnlyDictionary<string, double>? componentQuantities = null,
        DecisionVariable decisionVariables = DecisionVariable.All,
        int seed = -1)
    {
        ArgumentNullException.ThrowIfNull(damageSample);

        int count = damageSample.Count;
        int nComp = damageSample.NumberOfComponents;

        // Pre-build per-column consequence lookup for fast access inside loop.
        var consequenceLists = new List<ConsequenceFunction>[nComp];
        var quantities = new double[nComp];
        for (int c = 0; c < nComp; c++)
        {
            string id = damageSample.ComponentIds[c];
            consequenceLists[c] = _consequences.TryGetValue(id, out var fns) ? fns : [];
            quantities[c] = componentQuantities?.GetValueOrDefault(id, 1.0) ?? 1.0;
        }

        // Pre-generate consequence uniform samples [count × nComp].
        double[,] uniforms = new MonteCarloSampler().GenerateUniform(count, nComp, seed);

        var result = new LossSample(damageSample.ComponentIds, count);

        // Parallel evaluation with thread-local accumulation.
        Parallel.For(0, count,
            () => (cost: 0.0, time: 0.0, fat: 0.0, inj: 0.0),
            (i, _, acc) =>
            {
                double totalCost = 0.0, totalTime = 0.0, totalFat = 0.0, totalInj = 0.0;

                for (int c = 0; c < nComp; c++)
                {
                    int dsIdx = damageSample.DamageStates[i, c];
                    double qty = quantities[c];
                    double u = uniforms[i, c];

                    double compCost = 0.0;
                    foreach (var fn in consequenceLists[c])
                    {
                        if (fn.DamageStateIndex != dsIdx) continue;

                        double loss = fn.SampleLoss(u) * qty;
                        switch (fn.DecisionVariable)
                        {
                            case DecisionVariable.Cost when decisionVariables.HasFlag(DecisionVariable.Cost):
                                compCost += loss;
                                totalCost += loss;
                                break;
                            case DecisionVariable.Time when decisionVariables.HasFlag(DecisionVariable.Time):
                                totalTime += loss;
                                break;
                            case DecisionVariable.Casualties when decisionVariables.HasFlag(DecisionVariable.Casualties):
                                totalFat += loss;
                                totalInj += fn.Beta * qty;
                                break;
                        }
                    }
                    result.ComponentCosts[i, c] = compCost;
                }

                result.TotalCosts[i] = totalCost;
                result.TotalTimes[i] = totalTime;
                result.Fatalities[i] = totalFat;
                result.Injuries[i] = totalInj;

                return (totalCost, totalTime, totalFat, totalInj);
            },
            _ => { });

        return result;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Output of a <see cref="LossModel"/> calculation.</summary>
public sealed class LossSample(IReadOnlyList<string> componentIds, int count)
{
    public IReadOnlyList<string> ComponentIds { get; } = componentIds;
    public double[] TotalCosts { get; } = new double[count];
    public double[] TotalTimes { get; } = new double[count];
    public double[] Fatalities { get; } = new double[count];
    public double[] Injuries { get; } = new double[count];
    public double[,] ComponentCosts { get; } = new double[count, componentIds.Count];
    public int Count => TotalCosts.Length;
}
