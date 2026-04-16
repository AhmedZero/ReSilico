using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.UI.Models;
using ReSilico.UI.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class SimulationViewModel : ViewModelBase
{
    private readonly ISimulationService _simulationService;
    private readonly INavigationService _navigation;
    private readonly ResultsViewModel _resultsVm;
    private readonly InputConfigurationViewModel _inputVm;
    private readonly CorrelationMatrixViewModel _correlationVm;
    private readonly SimulationSettingsViewModel _settingsVm;
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> LogLines { get; } = [];

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
        ResultsViewModel resultsVm,
        InputConfigurationViewModel inputVm,
        CorrelationMatrixViewModel correlationVm,
        SimulationSettingsViewModel settingsVm)
    {
        _simulationService = simulationService;
        _navigation = navigation;
        _resultsVm = resultsVm;
        _inputVm = inputVm;
        _correlationVm = correlationVm;
        _settingsVm = settingsVm;
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
        if (!_inputVm.Validate() || !_settingsVm.Validate())
        {
            StatusMessage = string.IsNullOrWhiteSpace(_inputVm.ValidationMessage)
                ? _settingsVm.ValidationMessage
                : _inputVm.ValidationMessage;
            return;
        }

        _cts = new CancellationTokenSource();
        CanRun = false;
        RunSimulationCommand.NotifyCanExecuteChanged();
        IsBusy = true;
        Progress = 0;
        LogLines.Clear();
        StatusMessage = "Starting simulation...";

        try
        {
            var config = new SimulationConfiguration();
            _inputVm.ApplyTo(config);
            _correlationVm.ApplyTo(config);
            _settingsVm.ApplyTo(config);

            LogLines.Add($"Samples: {config.NumberOfRealizations:N0}");
            LogLines.Add($"Sampler: {(config.UseAdaptive ? "Adaptive Monte Carlo" : config.SamplingMethod)}");
            LogLines.Add($"Copula: {config.CopulaType}");
            LogLines.Add($"EDPs: {config.Demands.Count}, components: {config.Fragilities.Select(f => f.ComponentId).Distinct().Count()}");

            var result = await _simulationService.RunAsync(config, _cts.Token);
            StatusMessage = $"Done. Mean total loss = {result.MeanCost:N0} USD";
            LogLines.Add(StatusMessage);

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
            RunSimulationCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelSimulation() => _cts?.Cancel();
}
