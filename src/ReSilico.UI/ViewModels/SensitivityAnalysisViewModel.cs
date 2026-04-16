using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ReSilico.Analysis.Sensitivity;
using ReSilico.UI.Models;
using ReSilico.UI.Services;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class SensitivityAnalysisViewModel : ViewModelBase
{
    private readonly InputConfigurationViewModel _input;
    private readonly CorrelationMatrixViewModel _correlation;
    private readonly SimulationSettingsViewModel _settings;

    public string[] Methods { get; } = ["Local", "Global"];
    public string[] TargetMetrics { get; } = ["Mean", "CVaR", "Percentile"];
    public ObservableCollection<SensitivityRow> Rows { get; } = [];

    [ObservableProperty] private string _selectedMethod = "Local";
    [ObservableProperty] private string _selectedTargetMetric = "CVaR";
    [ObservableProperty] private int _sampleSize = 5_000;
    [ObservableProperty] private double _perturbationFactor = 0.05;
    [ObservableProperty] private double _targetQuantile = 0.95;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private ISeries[] _tornadoSeries = [];

    public Axis[] TornadoXAxes { get; } = [new Axis { Name = "Sensitivity" }];
    public Axis[] TornadoYAxes { get; } = [new Axis { Labels = [] }];

    public SensitivityAnalysisViewModel(
        InputConfigurationViewModel input,
        CorrelationMatrixViewModel correlation,
        SimulationSettingsViewModel settings)
    {
        _input = input;
        _correlation = correlation;
        _settings = settings;
        Title = "Sensitivity Analysis";
    }

    [RelayCommand]
    private async Task RunSensitivityAsync()
    {
        IsBusy = true;
        StatusMessage = "Running sensitivity analysis...";

        try
        {
            var config = new SimulationConfiguration();
            _input.ApplyTo(config);
            _correlation.ApplyTo(config);
            _settings.ApplyTo(config);
            config.NumberOfRealizations = SampleSize;

            var pipeline = SimulationModelFactory.BuildPipeline(config);
            var options = new SensitivityOptions
            {
                Method = SelectedMethod == "Global" ? SensitivityMethod.Global : SensitivityMethod.Local,
                TargetStatistic = SelectedTargetMetric switch
                {
                    "Mean" => SensitivityStatistic.Mean,
                    "Percentile" => SensitivityStatistic.Percentile,
                    _ => SensitivityStatistic.CVaR
                },
                TargetQuantile = TargetQuantile,
                PerturbationFactor = PerturbationFactor,
                SampleSize = SampleSize,
                Seed = _settings.RandomSeed
            };

            var result = await Task.Run(() => new SensitivityAnalyzer().Analyze(pipeline, options));
            LoadResult(result);
            StatusMessage = $"Baseline {result.TargetStatistic}: {result.BaselineValue:N0}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadResult(SensitivityResult result)
    {
        Rows.Clear();
        foreach (var entry in result.Ranked.Take(12))
        {
            Rows.Add(new SensitivityRow
            {
                Parameter = entry.Parameter,
                Group = entry.ParameterGroup,
                Sensitivity = entry.SensitivityValue,
                Relative = entry.RelativeSensitivity
            });
        }

        TornadoYAxes[0].Labels = Rows.Select(r => r.Parameter).Reverse().ToArray();
        TornadoSeries =
        [
            new RowSeries<double>
            {
                Values = Rows.Select(r => Math.Abs(r.Sensitivity)).Reverse().ToArray(),
                Name = SelectedTargetMetric,
                Fill = new SolidColorPaint(SKColor.Parse("#6BD5C9"))
            }
        ];
        HasResults = Rows.Count > 0;
    }
}

public class SensitivityRow
{
    public string Parameter { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public double Sensitivity { get; set; }
    public double Relative { get; set; }
    public string SensitivityFormatted => Sensitivity.ToString("N3");
    public string RelativeFormatted => Relative.ToString("N3");
}
