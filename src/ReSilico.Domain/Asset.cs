// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Asset (building or structure)

using ReSilico.Domain.Enums;

namespace ReSilico.Domain;

/// <summary>
/// A building or structural system subject to assessment.
/// Contains metadata and the inventory of <see cref="Component"/>s.
/// </summary>
public sealed class Asset
{
    private readonly List<Component> _components = [];

    public Asset(string id, string name, HazardType hazardType = HazardType.Earthquake)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Name = name;
        HazardType = hazardType;
    }

    /// <summary>Unique asset identifier.</summary>
    public string Id { get; }

    /// <summary>Human-readable asset name.</summary>
    public string Name { get; }

    public HazardType HazardType { get; }

    /// <summary>Total replacement cost (used as loss ceiling).</summary>
    public double ReplacementCost { get; set; }

    /// <summary>Total floor area (m² or ft²).</summary>
    public double FloorArea { get; set; }

    /// <summary>Number of stories.</summary>
    public int NumberOfStories { get; set; } = 1;

    /// <summary>Occupancy type (e.g., "Residential", "Commercial").</summary>
    public string OccupancyType { get; set; } = string.Empty;

    /// <summary>All performance-group components in this asset.</summary>
    public IReadOnlyList<Component> Components => _components;

    public void AddComponent(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _components.Add(component);
    }

    public void AddComponents(IEnumerable<Component> components)
    {
        foreach (var c in components) AddComponent(c);
    }

    /// <summary>All distinct EDPs referenced by any component in this asset.</summary>
    public IEnumerable<EDP> GetRequiredEdps() =>
        _components.Select(c => c.GoverningEdp).Distinct();

    public override string ToString() => $"{Id}: {Name} ({_components.Count} components)";
}
