using System.Collections.Generic;

namespace ReSilico.UI.Models;

public class SimulationConfiguration
{
    public int NumberOfRealizations { get; set; } = 10_000;
    public int RandomSeed { get; set; } = 42;
    public string SamplingMethod { get; set; } = "LatinHypercube";
    public string CopulaType { get; set; } = "Gaussian";
    public double DegreesOfFreedom { get; set; } = 6.0;
    public bool UseAdaptive { get; set; }
    public int InitialSamples { get; set; } = 5_000;
    public int BatchSize { get; set; } = 2_000;
    public double ToleranceMean { get; set; } = 0.02;
    public double ToleranceTail { get; set; } = 0.05;
    public double TargetQuantile { get; set; } = 0.95;
    public List<DistributionParameterSet> Demands { get; set; } = [];
    public List<FragilityParameterSet> Fragilities { get; set; } = [];
    public List<LossParameterSet> Losses { get; set; } = [];
    public double[,] CorrelationMatrix { get; set; } = new double[0, 0];
}
