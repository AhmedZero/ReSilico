// Unit name constants used in ReSilico CSV files.

namespace ReSilico.IO;

/// <summary>
/// Standard unit string constants written into the Units row of ReSilico CSV files.
/// </summary>
public static class Units
{
    // Dimensionless
    public const string Unitless  = "unitless";
    public const string Ea        = "ea";           // each (component count)
    public const string Rad       = "rad";           // radians (inter-story drift)

    // Acceleration
    public const string G         = "g";             // gravity
    public const string Inps2     = "inps2";         // in/s²
    public const string Mps2      = "mps2";          // m/s²

    // Length
    public const string M         = "m";
    public const string Mm        = "mm";
    public const string In        = "in";
    public const string Ft        = "ft";

    // Currency / loss
    public const string Usd       = "USD";
    public const string Usd2011   = "USD_2011";
    public const string LossRatio = "loss_ratio";   // fraction of replacement cost

    // Time
    public const string WorkerDay = "worker_day";
    public const string Day       = "day";
    public const string Hour      = "hour";

    /// <summary>
    /// Returns the standard unit string for a given EDP type.
    /// Defaults to <see cref="Unitless"/> for unrecognised types.
    /// </summary>
    public static string ForEdpType(string edpType) => edpType.ToUpperInvariant() switch
    {
        "PID" or "RID" => Rad,
        "PFA" or "SA"  => G,
        "PGA"          => G,
        "PGV"          => "inps",
        "PGD"          => In,
        _              => Unitless
    };
}
