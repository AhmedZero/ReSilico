using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.UI.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReSilico.UI.ViewModels;

public partial class InputConfigurationViewModel : ViewModelBase
{
    public ObservableCollection<DistributionParameterSet> Demands { get; } = [];
    public ObservableCollection<FragilityParameterSet> Fragilities { get; } = [];
    public ObservableCollection<LossParameterSet> Losses { get; } = [];

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public InputConfigurationViewModel()
    {
        Title = "Input Configuration";
        AddDefaultRows();
        Validate();
    }

    [RelayCommand]
    private void AddDemand()
    {
        var name = $"EDP-{Demands.Count + 1}-1";
        Demands.Add(new DistributionParameterSet
        {
            Name = name,
            DistributionType = "Lognormal",
            Param1 = 0.025,
            Param2 = 0.40
        });
        Fragilities.Add(new FragilityParameterSet { ComponentId = $"C-{name}", EdpName = name });
        Losses.Add(new LossParameterSet { ComponentId = $"C-{name}" });
        Validate();
    }

    [RelayCommand]
    private void RemoveDemand()
    {
        if (Demands.Count <= 1) return;
        var removed = Demands[^1];
        Demands.RemoveAt(Demands.Count - 1);
        RemoveRowsForDemand(removed.Name);
        Validate();
    }

    [RelayCommand]
    private void AddFragilityState()
    {
        var demand = Demands.FirstOrDefault();
        string edpName = demand?.Name ?? "PID-1-1";
        string componentId = $"C-{edpName}";
        int next = Fragilities.Count(f => f.ComponentId == componentId) + 1;
        Fragilities.Add(new FragilityParameterSet
        {
            ComponentId = componentId,
            EdpName = edpName,
            DamageState = $"DS{next}",
            Median = next * 0.03,
            Beta = 0.40
        });
        Losses.Add(new LossParameterSet
        {
            ComponentId = componentId,
            DamageState = $"DS{next}",
            MedianCost = next * 50_000,
            Beta = 0.50
        });
        Validate();
    }

    [RelayCommand]
    private void RemoveFragilityState()
    {
        if (Fragilities.Count <= 1) return;
        var row = Fragilities[^1];
        Fragilities.RemoveAt(Fragilities.Count - 1);
        var loss = Losses.LastOrDefault(l => l.ComponentId == row.ComponentId && l.DamageState == row.DamageState);
        if (loss is not null) Losses.Remove(loss);
        Validate();
    }

    public SimulationConfiguration ApplyTo(SimulationConfiguration config)
    {
        config.Demands = [.. Demands];
        config.Fragilities = [.. Fragilities];
        config.Losses = [.. Losses];
        return config;
    }

    public bool Validate()
    {
        if (Demands.Count == 0)
            return Fail("At least one demand EDP is required.");
        if (Demands.Any(d => string.IsNullOrWhiteSpace(d.Name)))
            return Fail("Every demand row needs an EDP name.");
        if (Demands.Any(d => d.Param1 <= 0 || d.Param2 <= 0))
            return Fail("Demand medians/means and dispersions must be positive.");
        if (Fragilities.Any(f => f.Median <= 0 || f.Beta <= 0))
            return Fail("Fragility medians and betas must be positive.");
        if (Losses.Any(l => l.MedianCost < 0 || l.Beta < 0))
            return Fail("Loss medians and betas must be non-negative.");

        ValidationMessage = string.Empty;
        return true;
    }

    private bool Fail(string message)
    {
        ValidationMessage = message;
        return false;
    }

    private void RemoveRowsForDemand(string name)
    {
        string componentId = $"C-{name}";
        foreach (var row in Fragilities.Where(f => f.EdpName == name || f.ComponentId == componentId).ToList())
            Fragilities.Remove(row);
        foreach (var row in Losses.Where(l => l.ComponentId == componentId).ToList())
            Losses.Remove(row);
    }

    private void AddDefaultRows()
    {
        Demands.Add(new DistributionParameterSet { Name = "PID-1-1", DistributionType = "Lognormal", Param1 = 0.025, Param2 = 0.40 });
        Demands.Add(new DistributionParameterSet { Name = "PID-2-1", DistributionType = "Lognormal", Param1 = 0.030, Param2 = 0.40 });
        Demands.Add(new DistributionParameterSet { Name = "PFA-1-1", DistributionType = "Lognormal", Param1 = 0.50, Param2 = 0.50 });

        foreach (var demand in Demands)
        {
            string componentId = $"C-{demand.Name}";
            Fragilities.Add(new FragilityParameterSet
            {
                ComponentId = componentId,
                EdpName = demand.Name,
                DamageState = "DS1",
                Median = demand.Name.StartsWith("PFA", System.StringComparison.OrdinalIgnoreCase) ? 0.60 : 0.03,
                Beta = 0.40
            });
            Losses.Add(new LossParameterSet
            {
                ComponentId = componentId,
                DamageState = "DS1",
                MedianCost = demand.Name.StartsWith("PFA", System.StringComparison.OrdinalIgnoreCase) ? 35_000 : 50_000,
                Beta = 0.50
            });
        }
    }
}
