// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Engineering Demand Parameter definition

namespace ReSilico.Domain;

/// <summary>
/// Engineering Demand Parameter (EDP): a structural response quantity (e.g., peak
/// inter-story drift ratio, peak floor acceleration) that links the hazard intensity
/// to component damage.
/// </summary>
public sealed class EDP
{
    /// <param name="type">EDP type identifier (e.g., "PID", "PFA", "SA").</param>
    /// <param name="location">Story number or node identifier.</param>
    /// <param name="direction">Loading direction (1 = X, 2 = Y, 0 = non-directional).</param>
    /// <param name="units">Physical unit string (e.g., "rad", "g", "in").</param>
    public EDP(string type, int location, int direction = 1, string units = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type;
        Location = location;
        Direction = direction;
        Units = units;
    }

    /// <summary>EDP type tag, e.g., "PID" (peak inter-story drift), "PFA" (peak floor accel).</summary>
    public string Type { get; }

    /// <summary>Story / node location index.</summary>
    public int Location { get; }

    /// <summary>Loading direction (1=X, 2=Y, 0=non-directional).</summary>
    public int Direction { get; }

    /// <summary>Physical units string (informational).</summary>
    public string Units { get; }

    /// <summary>Canonical key used to match EDPs against demand model variables.</summary>
    public string Key => $"{Type}-{Location}-{Direction}";

    public override string ToString() => Key;
    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);
    public override bool Equals(object? obj) =>
        obj is EDP other && Key == other.Key;
}
