using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.IO;
using ReSilico.UI.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ISimulationService _simulationService;
    private readonly IFileDialogService _fileDialogs;
    private readonly DistributionEditorViewModel _distEditor;
    private readonly ResultsViewModel _results;

    [ObservableProperty]
    private string _statusMessage = "Ready. Configure demands and run a simulation.";

    [ObservableProperty]
    private bool _hasResults;

    public DashboardViewModel(
        INavigationService navigation,
        ISimulationService simulationService,
        IFileDialogService fileDialogs,
        DistributionEditorViewModel distEditor,
        ResultsViewModel results)
    {
        _navigation       = navigation;
        _simulationService = simulationService;
        _fileDialogs      = fileDialogs;
        _distEditor       = distEditor;
        _results          = results;
        Title = "Dashboard";
    }

    [RelayCommand]
    private void OpenDistributionEditor() => _navigation.NavigateTo<DistributionEditorViewModel>();

    [RelayCommand]
    private void OpenCorrelationMatrix() => _navigation.NavigateTo<CorrelationMatrixViewModel>();

    [RelayCommand]
    private void OpenSimulation() => _navigation.NavigateTo<SimulationViewModel>();

    [RelayCommand]
    private void OpenResults() => _navigation.NavigateTo<ResultsViewModel>();

    // ── File commands ────────────────────────────────────────────────────────

    /// <summary>
    /// Open a saved assessment output folder.
    /// Loads demand marginals into the Distribution Editor and the loss sample into Results.
    /// </summary>
    [RelayCommand]
    private async Task OpenAssessmentAsync()
    {
        var folder = await _fileDialogs.PickOpenFolderAsync("Open Assessment Output Folder");
        if (folder is null) return;

        try
        {
            var snapshot = AssessmentIO.LoadResults(folder);

            if (snapshot.DemandModel?.Specs.Count > 0)
            {
                _distEditor.LoadSpecs(snapshot.DemandModel.Specs);
                StatusMessage = $"Loaded {snapshot.DemandModel.Specs.Count} EDP demand(s).";
            }

            if (snapshot.LossSample is not null)
            {
                _results.LoadFromLossSample(snapshot.LossSample);
                HasResults = true;
                StatusMessage += $" Loaded {snapshot.LossSample.Count} loss realizations.";
            }

            if (snapshot.DemandModel is null && snapshot.LossSample is null)
                StatusMessage = "No recognizable files found in the selected folder.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening assessment: {ex.Message}";
        }
    }

    /// <summary>
    /// Save current demand marginals (and the current loss sample if available)
    /// to a folder chosen by the user.
    /// </summary>
    [RelayCommand]
    private async Task SaveAssessmentAsync()
    {
        var folder = await _fileDialogs.PickOpenFolderAsync("Select Output Folder");
        if (folder is null) return;

        try
        {
            var specs = DistributionEditorViewModel.ToSpecs(_distEditor.Demands);
            DemandModelIO.SaveMarginals(Path.Combine(folder, "demand"), specs);

            if (_results.CurrentLossSample is not null)
                LossModelIO.SaveLossSample(Path.Combine(folder, "loss"), _results.CurrentLossSample);

            StatusMessage = $"Assessment saved to {folder}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving assessment: {ex.Message}";
        }
    }
}
