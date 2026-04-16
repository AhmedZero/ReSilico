using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.Analysis.Demand;
using ReSilico.IO;
using ReSilico.UI.Models;
using ReSilico.UI.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class DataViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogs;
    private readonly InputConfigurationViewModel _input;
    private readonly ResultsViewModel _results;

    public ObservableCollection<CsvPreviewRow> PreviewRows { get; } = [];

    [ObservableProperty]
    private string _statusMessage = "Choose a folder with ReSilico CSV files.";

    public DataViewModel(
        IFileDialogService fileDialogs,
        InputConfigurationViewModel input,
        ResultsViewModel results)
    {
        _fileDialogs = fileDialogs;
        _input = input;
        _results = results;
        Title = "CSV Data";
    }

    [RelayCommand]
    private async Task LoadFromFolderAsync()
    {
        var folder = await _fileDialogs.PickOpenFolderAsync("Load ReSilico CSV Folder");
        if (folder is null) return;

        try
        {
            var snapshot = AssessmentIO.LoadResults(folder);
            PreviewRows.Clear();

            if (snapshot.DemandModel is not null)
            {
                _input.Demands.Clear();
                foreach (var spec in snapshot.DemandModel.Specs)
                {
                    var row = new DistributionParameterSet
                    {
                        Name = spec.Edp.Key,
                        DistributionType = spec.Kind == EdpDistributionKind.Lognormal ? "Lognormal" : "Normal",
                        Param1 = spec.Kind == EdpDistributionKind.Lognormal ? Math.Exp(spec.Theta1) : spec.Theta1,
                        Param2 = spec.Theta2
                    };
                    _input.Demands.Add(row);
                    PreviewRows.Add(new CsvPreviewRow
                    {
                        Source = "demand_marginals.csv",
                        Name = row.Name,
                        Type = row.DistributionType,
                        Value1 = row.Param1.ToString("G4"),
                        Value2 = row.Param2.ToString("G4")
                    });
                }
            }

            if (snapshot.LossSample is not null)
            {
                _results.LoadFromLossSample(snapshot.LossSample);
                PreviewRows.Add(new CsvPreviewRow
                {
                    Source = "loss_sample.csv",
                    Name = "realizations",
                    Type = "LossSample",
                    Value1 = snapshot.LossSample.Count.ToString("N0"),
                    Value2 = $"{snapshot.LossSample.ComponentIds.Count} components"
                });
            }

            StatusMessage = PreviewRows.Count == 0
                ? "No recognized ReSilico CSV files were found."
                : $"Loaded {PreviewRows.Count} preview row(s) from {Path.GetFileName(folder)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveToFolderAsync()
    {
        var folder = await _fileDialogs.PickOpenFolderAsync("Save ReSilico CSV Folder");
        if (folder is null) return;

        try
        {
            var specs = DistributionEditorViewModel.ToSpecs(_input.Demands);
            DemandModelIO.SaveMarginals(Path.Combine(folder, "demand"), specs);

            if (_results.CurrentLossSample is not null)
                LossModelIO.SaveLossSample(Path.Combine(folder, "loss"), _results.CurrentLossSample);

            PreviewRows.Clear();
            foreach (var row in _input.Demands.Take(20))
            {
                PreviewRows.Add(new CsvPreviewRow
                {
                    Source = "demand_marginals.csv",
                    Name = row.Name,
                    Type = row.DistributionType,
                    Value1 = row.Param1.ToString("G4"),
                    Value2 = row.Param2.ToString("G4")
                });
            }
            StatusMessage = $"Saved demand CSV{(_results.CurrentLossSample is null ? string.Empty : " and loss sample")} to {folder}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
