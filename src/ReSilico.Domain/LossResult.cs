// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Aggregated loss result for a single Monte Carlo realisation

namespace ReSilico.Domain;

/// <summary>
/// Loss outcomes for a single simulation realisation.
/// </summary>
/// <param name="realisationIndex">0-based index of the Monte Carlo realisation.</param>
public sealed class LossResult(int realisationIndex)
{

    /// <summary>Monte Carlo realisation index.</summary>
    public int RealisationIndex { get; } = realisationIndex;

    /// <summary>Total repair / replacement cost (same units as Asset.ReplacementCost).</summary>
    public double TotalCost { get; set; }

    /// <summary>Total repair time (days).</summary>
    public double TotalTime { get; set; }

    /// <summary>Number of fatalities (expected).</summary>
    public double Fatalities { get; set; }

    /// <summary>Number of injuries (expected).</summary>
    public double Injuries { get; set; }

    /// <summary>True if global collapse was realised in this simulation.</summary>
    public bool IsCollapse { get; set; }

    /// <summary>Per-component damage state indices (component ID → DS index).</summary>
    public Dictionary<string, int> ComponentDamageStates { get; } = [];

    /// <summary>Per-component loss contributions (component ID → cost).</summary>
    public Dictionary<string, double> ComponentCosts { get; } = [];
}
