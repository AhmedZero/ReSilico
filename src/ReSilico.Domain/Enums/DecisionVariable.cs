namespace ReSilico.Domain.Enums;

/// <summary>Quantitative output tracked by the loss model.</summary>
[Flags]
public enum DecisionVariable
{
    None = 0,

    /// <summary>Repair / replacement cost (monetary).</summary>
    Cost = 1,

    /// <summary>Downtime / repair time (days or person-days).</summary>
    Time = 2,

    /// <summary>Casualties: injuries and fatalities.</summary>
    Casualties = 4,

    /// <summary>All decision variables.</summary>
    All = Cost | Time | Casualties,
}
