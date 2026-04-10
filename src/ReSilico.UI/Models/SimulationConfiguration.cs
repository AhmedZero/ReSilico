using System.Collections.Generic;

namespace ReSilico.UI.Models;

public class SimulationConfiguration
{
    public int NumberOfRealizations { get; set; } = 10_000;
    public int RandomSeed { get; set; } = 42;
    public string SamplingMethod { get; set; } = "LatinHypercube";
    public List<DistributionParameterSet> Demands { get; set; } = [];
    public double[,] CorrelationMatrix { get; set; } = new double[0, 0];
}
