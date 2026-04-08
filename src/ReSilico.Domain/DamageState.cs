// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Damage state definition

namespace ReSilico.Domain;

/// <summary>
/// Discrete damage state for a structural or non-structural component.
/// Damage states are ordered: DS0 = undamaged, DS1 … DSn = increasing damage.
/// </summary>
public sealed class DamageState
{
    /// <param name="index">0-based index (0 = undamaged).</param>
    /// <param name="label">Human-readable label, e.g., "No Damage", "Minor", "Collapse".</param>
    /// <param name="description">Optional long description.</param>
    public DamageState(int index, string label, string description = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Index = index;
        Label = label;
        Description = description;
    }

    /// <summary>0-based index. DS0 is the undamaged state.</summary>
    public int Index { get; }

    /// <summary>Short label used in output tables.</summary>
    public string Label { get; }

    public string Description { get; }

    public bool IsUndamaged => Index == 0;

    public override string ToString() => $"DS{Index}: {Label}";
}
