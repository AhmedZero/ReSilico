using System.Text.Json.Serialization;

namespace ReSilico.UI.Models;

public class DistributionParameterSet
{
    public string Name { get; set; } = string.Empty;
    public string DistributionType { get; set; } = "Lognormal";
    public double Param1 { get; set; } = 0.5;   // mean / mu
    public double Param2 { get; set; } = 0.4;   // std / beta
    public bool IsTruncated { get; set; }
    public double? LowerBound { get; set; }
    public double? UpperBound { get; set; }
}
