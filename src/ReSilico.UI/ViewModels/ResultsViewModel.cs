using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.IO;
using ReSilico.UI.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class ResultsViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogs;

    // ── Summary statistics ───────────────────────────────────────────────────
    [ObservableProperty] private double _meanCost;
    [ObservableProperty] private double _stdDevCost;
    [ObservableProperty] private double _medianCost;
    [ObservableProperty] private double _coeffOfVariation;
    [ObservableProperty] private double _p5Cost;
    [ObservableProperty] private double _p95Cost;
    [ObservableProperty] private double _meanTime;

    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _fileStatusMessage = string.Empty;

    // ── Charts ───────────────────────────────────────────────────────────────
    [ObservableProperty] private ISeries[] _histogramSeries = [];
    [ObservableProperty] private ISeries[] _exceedanceSeries = [];

    public Axis[] HistXAxes { get; } = [new Axis { Name = "Total Loss (USD)" }];
    public Axis[] HistYAxes { get; } = [new Axis { Name = "Count" }];
    public Axis[] ExcXAxes  { get; } = [new Axis { Name = "Total Loss (USD)" }];
    public Axis[] ExcYAxes  { get; } = [new Axis { Name = "P(Loss > x)" }];

    // ── Component table ──────────────────────────────────────────────────────
    public ObservableCollection<ComponentLossRow> ComponentLosses { get; } = [];

    // ── Held sample (for saving) ─────────────────────────────────────────────
    public LossSample? CurrentLossSample { get; private set; }

    public ResultsViewModel(IFileDialogService fileDialogs)
    {
        _fileDialogs = fileDialogs;
        Title = "Simulation Results";
    }

    // ── Load from SimulationResult (in-process run) ──────────────────────────

    public void LoadResult(SimulationResult result)
    {
        CurrentLossSample = result.LossSample;

        MeanCost       = result.MeanCost;
        StdDevCost     = result.StdDevCost;
        MedianCost     = result.MedianCost;
        CoeffOfVariation = result.CoeffOfVariationCost;
        P5Cost         = result.CostAtPercentile(0.05);
        P95Cost        = result.CostAtPercentile(0.95);
        MeanTime       = result.MeanTime;
        HasResults     = true;

        BuildHistogram(result.LossSample.TotalCosts);
        BuildExceedanceCurve(result.LossSample.TotalCosts, result.NumberOfSimulations);
        BuildComponentTable(result.MeanCostByComponent());
    }

    // ── Load from LossSample (from disk) ────────────────────────────────────

    public void LoadFromLossSample(LossSample sample)
    {
        CurrentLossSample = sample;
        int n = sample.Count;

        double[] costs = sample.TotalCosts;
        double[] times = sample.TotalTimes;

        MeanCost         = ComputeMean(costs);
        StdDevCost       = ComputeStdDev(costs);
        MedianCost       = ComputePercentile(costs, 0.50);
        CoeffOfVariation = MeanCost > 0 ? StdDevCost / MeanCost : 0;
        P5Cost           = ComputePercentile(costs, 0.05);
        P95Cost          = ComputePercentile(costs, 0.95);
        MeanTime         = ComputeMean(times);
        HasResults       = true;

        BuildHistogram(costs);
        BuildExceedanceCurve(costs, n);
        BuildComponentTable(ComputeMeanByComponent(sample));
    }

    // ── File commands ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadFromFolderAsync()
    {
        var folder = await _fileDialogs.PickOpenFolderAsync("Open Assessment Output Folder");
        if (folder is null) return;

        try
        {
            var snapshot = AssessmentIO.LoadResults(folder);
            if (snapshot.LossSample is null)
            {
                FileStatusMessage = "No loss_sample.csv found in the selected folder.";
                return;
            }
            LoadFromLossSample(snapshot.LossSample);
            FileStatusMessage = $"Loaded {snapshot.LossSample.Count} realizations from {Path.GetFileName(folder)}.";
        }
        catch (Exception ex)
        {
            FileStatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveToFolderAsync()
    {
        if (CurrentLossSample is null) return;

        var folder = await _fileDialogs.PickOpenFolderAsync("Select Output Folder");
        if (folder is null) return;

        try
        {
            LossModelIO.SaveLossSample(Path.Combine(folder, "loss"), CurrentLossSample);
            FileStatusMessage = $"Saved to {folder}.";
        }
        catch (Exception ex)
        {
            FileStatusMessage = $"Error: {ex.Message}";
        }
    }

    // ── Chart builders ───────────────────────────────────────────────────────

    private void BuildHistogram(double[] costs)
    {
        const int bins = 25;
        var (edges, counts) = ComputeHistogram(costs, bins);
        HistXAxes[0].Labels = edges.SkipLast(1).Select(e => $"{e / 1_000:N0}k").ToArray();

        HistogramSeries =
        [
            new ColumnSeries<double>
            {
                Values = counts.Select(c => (double)c).ToArray(),
                Name = "Loss Count",
                Fill = new SolidColorPaint(SKColors.SteelBlue)
            }
        ];
    }

    private void BuildExceedanceCurve(double[] costs, int n)
    {
        double lo = ComputePercentile(costs, 0.01);
        double hi = ComputePercentile(costs, 0.99);
        if (hi <= lo) hi = lo + 1;
        int pts = 50;
        double step = (hi - lo) / pts;

        var points = new ObservablePoint[pts + 1];
        for (int i = 0; i <= pts; i++)
        {
            double threshold = lo + i * step;
            double prob = costs.Count(c => c > threshold) / (double)n;
            points[i] = new ObservablePoint(threshold, prob);
        }

        ExceedanceSeries =
        [
            new LineSeries<ObservablePoint>
            {
                Values = points,
                Name = "P(Loss > x)",
                GeometrySize = 0,
                LineSmoothness = 1,
                Fill = null
            }
        ];
    }

    private void BuildComponentTable(IReadOnlyDictionary<string, double> byComponent)
    {
        ComponentLosses.Clear();
        foreach (var (id, mean) in byComponent.OrderByDescending(kv => kv.Value))
            ComponentLosses.Add(new ComponentLossRow { ComponentId = id, MeanLoss = mean });
    }

    // ── Statistics helpers ───────────────────────────────────────────────────

    private static double ComputeMean(double[] arr) =>
        arr.Length == 0 ? 0 : arr.Average();

    private static double ComputeStdDev(double[] arr)
    {
        if (arr.Length < 2) return 0;
        double mean = arr.Average();
        double variance = arr.Sum(x => (x - mean) * (x - mean)) / (arr.Length - 1);
        return Math.Sqrt(variance);
    }

    private static double ComputePercentile(double[] arr, double p)
    {
        if (arr.Length == 0) return 0;
        var sorted = arr.OrderBy(x => x).ToArray();
        double idx = p * (sorted.Length - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
    }

    private static (double[] edges, int[] counts) ComputeHistogram(double[] costs, int bins)
    {
        if (costs.Length == 0) return (new double[bins + 1], new int[bins]);
        double min = costs.Min(), max = costs.Max();
        if (max <= min) max = min + 1.0;
        double w = (max - min) / bins;
        var counts = new int[bins];
        var edges  = new double[bins + 1];
        for (int k = 0; k <= bins; k++) edges[k] = min + k * w;
        foreach (double c in costs)
        {
            int b = Math.Min((int)((c - min) / w), bins - 1);
            counts[b]++;
        }
        return (edges, counts);
    }

    private static IReadOnlyDictionary<string, double> ComputeMeanByComponent(LossSample sample)
    {
        int n = sample.Count;
        int nComp = sample.ComponentIds.Count;
        var result = new Dictionary<string, double>(nComp);
        for (int c = 0; c < nComp; c++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += sample.ComponentCosts[i, c];
            result[sample.ComponentIds[c]] = n > 0 ? sum / n : 0;
        }
        return result;
    }
}

public class ComponentLossRow
{
    public string ComponentId { get; set; } = string.Empty;
    public double MeanLoss { get; set; }
    public string MeanLossFormatted => $"{MeanLoss:N0}";
}
