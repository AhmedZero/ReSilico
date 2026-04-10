using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.UI.Models;
using ReSilico.UI.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class SimulationViewModel : ViewModelBase
{
    private readonly ISimulationService _simulationService;
    private readonly INavigationService _navigation;
    private readonly ResultsViewModel _resultsVm;
    private CancellationTokenSource? _cts;

    // ── Configuration ────────────────────────────────────────────────────────
    [ObservableProperty]
    private int _numberOfRealizations = 10_000;

    [ObservableProperty]
    private int _randomSeed = 42;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLhs))]
    private string _selectedSamplingMethod = "LatinHypercube";

    public string[] SamplingMethods { get; } = ["LatinHypercube", "MonteCarlo"];
    public bool IsLhs => SelectedSamplingMethod == "LatinHypercube";

    // ── Progress / status ────────────────────────────────────────────────────
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private bool _canRun = true;

    public SimulationViewModel(
        ISimulationService simulationService,
        INavigationService navigation,
        ResultsViewModel resultsVm)
    {
        _simulationService = simulationService;
        _navigation = navigation;
        _resultsVm = resultsVm;
        Title = "Run Simulation";

        _simulationService.ProgressChanged += (_, p) =>
        {
            Progress = p * 100.0;
            StatusMessage = p < 1.0
                ? $"Running... {p * 100:F0}%"
                : "Simulation complete.";
        };
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunSimulationAsync()
    {
        _cts = new CancellationTokenSource();
        CanRun = false;
        IsBusy = true;
        Progress = 0;
        StatusMessage = "Starting simulation…";

        try
        {
            var config = new SimulationConfiguration
            {
                NumberOfRealizations = NumberOfRealizations,
                RandomSeed = RandomSeed,
                SamplingMethod = SelectedSamplingMethod
            };

            var result = await _simulationService.RunAsync(config, _cts.Token);
            StatusMessage = $"Done. Mean total loss = {result.MeanCost:N0} USD";

            _resultsVm.LoadResult(result);
            _navigation.NavigateTo(_resultsVm);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Simulation cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            CanRun = true;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelSimulation() => _cts?.Cancel();
}
