using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using ReSilico.Analysis.Demand;
using ReSilico.Core.Distributions;
using ReSilico.Core.RandomVariables;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.IO;
using ReSilico.UI.Models;
using ReSilico.UI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ReSilico.UI.ViewModels;

public partial class DistributionEditorViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogs;

    // ── Demand list ──────────────────────────────────────────────────────────
    public ObservableCollection<DistributionParameterSet> Demands { get; } = [];

    [ObservableProperty]
    private DistributionParameterSet? _selectedDemand;

    // ── Parameters (bound to editor panel) ──────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLognormal))]
    private string _selectedDistributionType = "Lognormal";

    [ObservableProperty]
    private double _param1 = 0.025;

    [ObservableProperty]
    private double _param2 = 0.40;

    [ObservableProperty]
    private string _demandName = "PID-1";

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _fileStatusMessage = string.Empty;

    // ── Chart ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private ISeries[] _pdfSeries = [];

    public Axis[] XAxes { get; } = [new Axis { Name = "x" }];
    public Axis[] YAxes { get; } = [new Axis { Name = "PDF" }];

    public string[] DistributionTypes { get; } = ["Lognormal", "Normal"];
    public bool IsLognormal => SelectedDistributionType == "Lognormal";

    public DistributionEditorViewModel(IFileDialogService fileDialogs)
    {
        _fileDialogs = fileDialogs;
        Title = "Distribution Editor";
        AddDefaultDemands();
        SelectedDemand = Demands.FirstOrDefault();
        if (SelectedDemand != null) LoadFromSelected();
        UpdateChart();
    }

    partial void OnParam1Changed(double value) { Validate(); UpdateChart(); }
    partial void OnParam2Changed(double value) { Validate(); UpdateChart(); }
    partial void OnSelectedDistributionTypeChanged(string value) { Validate(); UpdateChart(); }
    partial void OnSelectedDemandChanged(DistributionParameterSet? value) { if (value != null) LoadFromSelected(); }

    private void LoadFromSelected()
    {
        if (SelectedDemand is null) return;
        DemandName = SelectedDemand.Name;
        SelectedDistributionType = SelectedDemand.DistributionType;
        Param1 = SelectedDemand.Param1;
        Param2 = SelectedDemand.Param2;
        UpdateChart();
    }

    [RelayCommand]
    private void ApplyParameters()
    {
        if (SelectedDemand is null || !string.IsNullOrEmpty(ValidationMessage)) return;
        SelectedDemand.Name = DemandName;
        SelectedDemand.DistributionType = SelectedDistributionType;
        SelectedDemand.Param1 = Param1;
        SelectedDemand.Param2 = Param2;
    }

    [RelayCommand]
    private void AddDemand()
    {
        var d = new DistributionParameterSet
        {
            Name = $"EDP-{Demands.Count + 1}",
            DistributionType = "Lognormal",
            Param1 = 0.025,
            Param2 = 0.4
        };
        Demands.Add(d);
        SelectedDemand = d;
    }

    [RelayCommand]
    private void RemoveDemand()
    {
        if (SelectedDemand is null || Demands.Count <= 1) return;
        Demands.Remove(SelectedDemand);
        SelectedDemand = Demands.LastOrDefault();
    }

    // ── File IO ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadFromFileAsync()
    {
        var path = await _fileDialogs.PickOpenFileAsync(
            "Open Demand Marginals",
            new FileFilter("CSV Files", ["*.csv"]),
            new FileFilter("All Files", ["*.*"]));

        if (path is null) return;

        try
        {
            // Accept both "<prefix>_marginals.csv" and a bare prefix path
            string prefix = path.EndsWith("_marginals.csv", StringComparison.OrdinalIgnoreCase)
                ? path[..^"_marginals.csv".Length]
                : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));

            var specs = DemandModelIO.LoadMarginals(prefix);
            LoadSpecs(specs);
            FileStatusMessage = $"Loaded {specs.Count} EDP(s) from {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            FileStatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveToFileAsync()
    {
        var path = await _fileDialogs.PickSaveFileAsync(
            "Save Demand Marginals",
            "csv",
            new FileFilter("CSV Files", ["*.csv"]));

        if (path is null) return;

        try
        {
            string prefix = path.EndsWith("_marginals.csv", StringComparison.OrdinalIgnoreCase)
                ? path[..^"_marginals.csv".Length]
                : path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? path[..^4]
                    : path;

            var specs = ToSpecs(Demands);
            DemandModelIO.SaveMarginals(prefix, specs);
            FileStatusMessage = $"Saved {specs.Count} EDP(s) to {Path.GetFileName(prefix)}_marginals.csv.";
        }
        catch (Exception ex)
        {
            FileStatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>Replace the current demand list with specs loaded from IO layer.</summary>
    public void LoadSpecs(IReadOnlyList<EdpDistributionSpec> specs)
    {
        Demands.Clear();
        foreach (var spec in specs)
        {
            Demands.Add(new DistributionParameterSet
            {
                Name = spec.Edp.Key,
                DistributionType = spec.Kind == EdpDistributionKind.Lognormal ? "Lognormal" : "Normal",
                // Store as the value the user sees: median (lognormal) or mean (normal)
                Param1 = spec.Kind == EdpDistributionKind.Lognormal
                    ? Math.Exp(spec.Theta1)
                    : spec.Theta1,
                Param2 = spec.Theta2
            });
        }
        SelectedDemand = Demands.FirstOrDefault();
    }

    /// <summary>Convert the current demand list to EdpDistributionSpec for the IO layer.</summary>
    public static List<EdpDistributionSpec> ToSpecs(IEnumerable<DistributionParameterSet> demands)
    {
        var result = new List<EdpDistributionSpec>();
        foreach (var d in demands)
        {
            var parts = ReSilicoCsv.SplitKey(d.Name);
            string type = parts[0];
            int loc = parts.Length > 1 && int.TryParse(parts[1], out var l) ? l : 1;
            int dir = parts.Length > 2 && int.TryParse(parts[2], out var di) ? di : 1;

            var edp  = new EDP(type, loc, dir);
            var kind = d.DistributionType == "Normal"
                ? EdpDistributionKind.Normal
                : EdpDistributionKind.Lognormal;

            double theta1 = kind == EdpDistributionKind.Lognormal
                ? Math.Log(d.Param1)
                : d.Param1;

            result.Add(new EdpDistributionSpec(edp, kind, theta1, d.Param2));
        }
        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void Validate()
    {
        if (Param2 <= 0)
        {
            ValidationMessage = "Dispersion/StdDev must be > 0.";
            return;
        }
        if (IsLognormal && Param1 <= 0)
        {
            ValidationMessage = "Median must be > 0 for Lognormal.";
            return;
        }
        ValidationMessage = string.Empty;
    }

    private void UpdateChart()
    {
        if (!string.IsNullOrEmpty(ValidationMessage)) return;

        try
        {
            IUnivariateDistribution dist = SelectedDistributionType == "Normal"
                ? new NormalDistribution(mean: Param1, standardDeviation: Param2)
                : new LognormalDistribution(muLn: Math.Log(Param1), sigmaLn: Param2);

            double lo = dist.InverseCDF(0.005);
            double hi = dist.InverseCDF(0.995);
            int pts = 200;
            double step = (hi - lo) / pts;

            var points = new ObservablePoint[pts + 1];
            for (int i = 0; i <= pts; i++)
            {
                double x = lo + i * step;
                points[i] = new ObservablePoint(x, dist.PDF(x));
            }

            PdfSeries =
            [
                new LineSeries<ObservablePoint>
                {
                    Values = points,
                    Name = $"{SelectedDistributionType} PDF",
                    GeometrySize = 0,
                    LineSmoothness = 1
                }
            ];
        }
        catch { /* swallow during parameter edits */ }
    }

    private void AddDefaultDemands()
    {
        Demands.Add(new DistributionParameterSet { Name = "PID-1-1", DistributionType = "Lognormal", Param1 = 0.025, Param2 = 0.40 });
        Demands.Add(new DistributionParameterSet { Name = "PID-2-1", DistributionType = "Lognormal", Param1 = 0.030, Param2 = 0.40 });
        Demands.Add(new DistributionParameterSet { Name = "PID-3-1", DistributionType = "Lognormal", Param1 = 0.028, Param2 = 0.40 });
        Demands.Add(new DistributionParameterSet { Name = "PFA-1-1", DistributionType = "Lognormal", Param1 = 0.50,  Param2 = 0.50 });
    }
}
