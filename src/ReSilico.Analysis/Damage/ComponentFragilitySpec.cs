// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Per-component fragility specification

using ReSilico.Domain;

namespace ReSilico.Analysis.Damage;

/// <summary>
/// Associates a <see cref="Component"/> with its ordered list of limit-state
/// <see cref="FragilityFunction"/>s (one per damage state transition DS₀→DS₁,
/// DS₁→DS₂, …).
///
/// Convention:
///   FragilityFunctions[k] gives P(DS ≥ k+1 | EDP), so the probability of being
///   in damage state k is:
///       P(DS = 0)   = 1 − P(DS ≥ 1)
///       P(DS = k)   = P(DS ≥ k) − P(DS ≥ k+1)     for k ≥ 1
///       P(DS = n)   = P(DS ≥ n)                    for the highest state
/// </summary>
public sealed class ComponentFragilitySpec
{
    private readonly List<FragilityFunction> _fragilityFunctions = [];

    public ComponentFragilitySpec(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        Component = component;
    }

    public Component Component { get; }

    /// <summary>
    /// Ordered fragility functions F₁, F₂, … each giving P(DS ≥ k | EDP).
    /// Must be added in ascending limit-state order.
    /// </summary>
    public IReadOnlyList<FragilityFunction> FragilityFunctions => _fragilityFunctions;

    /// <summary>Number of limit states (= number of non-zero damage states).</summary>
    public int NumberOfLimitStates => _fragilityFunctions.Count;

    public void AddFragilityFunction(FragilityFunction f)
    {
        ArgumentNullException.ThrowIfNull(f);
        _fragilityFunctions.Add(f);
    }

    /// <summary>
    /// Determine the realised damage state index for a single EDP value and a
    /// vector of independent U(0,1) capacity samples (one per limit state).
    ///
    /// The algorithm is progressive: a higher limit state can only be reached
    /// if all lower ones are also exceeded (consistent with pelicun / FEMA P-58).
    /// </summary>
    /// <param name="edp">Realised EDP value.</param>
    /// <param name="uniformCapacitySamples">
    ///   Uniform [0,1] samples — one per limit state.  These are fixed per realisation
    ///   to maintain within-realisation consistency.
    /// </param>
    /// <returns>Realised damage state index (0 = undamaged, …).</returns>
    public int EvaluateDamageState(double edp, ReadOnlySpan<double> uniformCapacitySamples)
    {
        int ds = 0;
        for (int k = 0; k < _fragilityFunctions.Count; k++)
        {
            if (_fragilityFunctions[k].IsExceeded(edp, uniformCapacitySamples[k]))
                ds = k + 1;
            else
                break;   // Can't exceed a higher LS if this one isn't exceeded.
        }
        return ds;
    }
}
