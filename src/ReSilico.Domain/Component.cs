// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Structural / non-structural component

using ReSilico.Domain.Enums;

namespace ReSilico.Domain;

/// <summary>
/// A structural or non-structural performance group member.
/// Each Component references the EDP that governs its response, carries a
/// quantity (e.g., number of units), and owns its damage state definitions.
/// </summary>
public sealed class Component
{
    private readonly List<DamageState> _damageStates;

    /// <param name="id">Unique component identifier (e.g., FEMA P-58 performance group ID).</param>
    /// <param name="governingEdp">EDP that drives the damage assessment for this component.</param>
    /// <param name="quantity">Number of units of this component (e.g., number of frames).</param>
    /// <param name="location">Story / node where the component resides.</param>
    /// <param name="direction">Loading direction (1=X, 2=Y, 0=non-directional).</param>
    public Component(
        string id,
        EDP governingEdp,
        double quantity = 1.0,
        int location = 1,
        int direction = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(governingEdp);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0d, nameof(quantity));

        Id = id;
        GoverningEdp = governingEdp;
        Quantity = quantity;
        Location = location;
        Direction = direction;
        _damageStates = [];
    }

    /// <summary>Unique identifier (e.g., "B1033.001a").</summary>
    public string Id { get; }

    /// <summary>EDP type that governs damage (linked to demand model).</summary>
    public EDP GoverningEdp { get; }

    /// <summary>Number of component units.</summary>
    public double Quantity { get; set; }

    public int Location { get; }
    public int Direction { get; }

    /// <summary>Ordered list of damage states (ascending severity).</summary>
    public IReadOnlyList<DamageState> DamageStates => _damageStates;

    public void AddDamageState(DamageState ds) => _damageStates.Add(ds);

    public override string ToString() => $"{Id} @ {GoverningEdp.Key} × {Quantity}";
}
