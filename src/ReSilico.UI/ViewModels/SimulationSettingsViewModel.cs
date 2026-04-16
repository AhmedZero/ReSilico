using CommunityToolkit.Mvvm.ComponentModel;
using ReSilico.UI.Models;

namespace ReSilico.UI.ViewModels;

public partial class SimulationSettingsViewModel : ViewModelBase
{
    public string[] SamplingMethods { get; } = ["MonteCarlo", "LatinHypercube", "Adaptive"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdaptive))]
    private string _selectedSamplingMethod = "LatinHypercube";

    [ObservableProperty] private int _numberOfRealizations = 10_000;
    [ObservableProperty] private int _randomSeed = 42;
    [ObservableProperty] private int _initialSamples = 5_000;
    [ObservableProperty] private int _batchSize = 2_000;
    [ObservableProperty] private double _toleranceMean = 0.02;
    [ObservableProperty] private double _toleranceTail = 0.05;
    [ObservableProperty] private double _targetQuantile = 0.95;
    [ObservableProperty] private string _validationMessage = string.Empty;

    public bool IsAdaptive => SelectedSamplingMethod == "Adaptive";

    public SimulationSettingsViewModel()
    {
        Title = "Simulation Settings";
    }

    public SimulationConfiguration ApplyTo(SimulationConfiguration config)
    {
        config.NumberOfRealizations = NumberOfRealizations;
        config.RandomSeed = RandomSeed;
        config.UseAdaptive = IsAdaptive;
        config.SamplingMethod = IsAdaptive ? "LatinHypercube" : SelectedSamplingMethod;
        config.InitialSamples = InitialSamples;
        config.BatchSize = BatchSize;
        config.ToleranceMean = ToleranceMean;
        config.ToleranceTail = ToleranceTail;
        config.TargetQuantile = TargetQuantile;
        return config;
    }

    public bool Validate()
    {
        if (NumberOfRealizations < 10)
            return Fail("Sample size must be at least 10.");
        if (IsAdaptive && InitialSamples > NumberOfRealizations)
            return Fail("Initial samples must be less than or equal to the sample cap.");
        if (IsAdaptive && (InitialSamples < 10 || BatchSize < 1))
            return Fail("Adaptive initial samples must be >= 10 and batch size must be >= 1.");
        if (ToleranceMean <= 0 || ToleranceTail <= 0)
            return Fail("Adaptive tolerances must be positive.");
        if (TargetQuantile <= 0 || TargetQuantile >= 1)
            return Fail("Target quantile must be between 0 and 1.");

        ValidationMessage = string.Empty;
        return true;
    }

    private bool Fail(string message)
    {
        ValidationMessage = message;
        return false;
    }
}
