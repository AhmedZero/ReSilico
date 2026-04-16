using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Copulas;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using ReSilico.UI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReSilico.UI.Services;

public static class SimulationModelFactory
{
    public static SimulationPipeline BuildPipeline(SimulationConfiguration config)
    {
        var demands = config.Demands.Count > 0 ? config.Demands : GetDefaultDemands();
        var fragilities = config.Fragilities.Count > 0 ? config.Fragilities : GetDefaultFragilities(demands);
        var losses = config.Losses.Count > 0 ? config.Losses : GetDefaultLosses(fragilities);
        var asset = BuildAsset(demands, fragilities);
        var sampler = CreateSampler(config.SamplingMethod);

        return new SimulationPipeline(asset)
            .WithDemand(BuildDemandModel(demands, config, sampler))
            .WithDamage(BuildDamageModel(asset, fragilities))
            .WithLoss(BuildLossModel(losses))
            .WithSampler(sampler);
    }

    public static AdaptiveOptions BuildAdaptiveOptions(SimulationConfiguration config) =>
        new()
        {
            InitialSamples = config.InitialSamples,
            BatchSize = config.BatchSize,
            MaxSamples = config.NumberOfRealizations,
            ToleranceMean = config.ToleranceMean,
            ToleranceTail = config.ToleranceTail,
            TargetQuantile = config.TargetQuantile,
            Seed = config.RandomSeed
        };

    private static ISampler CreateSampler(string samplingMethod) =>
        samplingMethod == "MonteCarlo" ? new MonteCarloSampler() : LatinHypercubeSampler.Standard;

    private static Asset BuildAsset(
        IReadOnlyList<DistributionParameterSet> demands,
        IReadOnlyList<FragilityParameterSet> fragilities)
    {
        var asset = new Asset("ui-asset", "UI Asset");
        var byComponent = fragilities
            .GroupBy(f => string.IsNullOrWhiteSpace(f.ComponentId) ? $"C-{f.EdpName}" : f.ComponentId);

        foreach (var group in byComponent)
        {
            string edpName = group.First().EdpName;
            if (string.IsNullOrWhiteSpace(edpName))
                edpName = demands.FirstOrDefault()?.Name ?? "PID-1-1";

            var component = new Component(group.Key, CreateEdp(edpName));
            int index = 1;
            foreach (var row in group)
            {
                component.AddDamageState(new DamageState(index, row.DamageState, row.DamageState));
                index++;
            }
            asset.AddComponent(component);
        }

        return asset;
    }

    private static DemandModel BuildDemandModel(
        IReadOnlyList<DistributionParameterSet> demands,
        SimulationConfiguration config,
        ISampler sampler)
    {
        var model = new DemandModel(sampler);

        foreach (var d in demands)
        {
            var kind = d.DistributionType == "Normal"
                ? EdpDistributionKind.Normal
                : EdpDistributionKind.Lognormal;

            double theta1 = kind == EdpDistributionKind.Lognormal ? Math.Log(d.Param1) : d.Param1;
            model.AddEdp(new EdpDistributionSpec(CreateEdp(d.Name), kind, theta1, d.Param2));
        }

        var rho = config.CorrelationMatrix.GetLength(0) == demands.Count
            ? config.CorrelationMatrix
            : BuildIdentity(demands.Count);

        if (config.CopulaType == "t-Copula")
            model.SetCopula(new TCopula(config.DegreesOfFreedom, rho, config.RandomSeed));
        else
            model.SetCorrelation(rho);

        return model;
    }

    private static DamageModel BuildDamageModel(
        Asset asset,
        IReadOnlyList<FragilityParameterSet> fragilities)
    {
        var model = new DamageModel();

        foreach (var component in asset.Components)
        {
            var spec = new ComponentFragilitySpec(component);
            foreach (var row in fragilities.Where(f => f.ComponentId == component.Id))
                spec.AddFragilityFunction(new FragilityFunction(row.Median, row.Beta, row.DamageState));

            if (spec.FragilityFunctions.Count == 0)
                spec.AddFragilityFunction(new FragilityFunction(0.03, 0.4, "DS1"));

            model.Add(spec);
        }

        return model;
    }

    private static LossModel BuildLossModel(IReadOnlyList<LossParameterSet> losses)
    {
        var model = new LossModel();
        foreach (var group in losses.GroupBy(l => l.ComponentId))
        {
            int index = 1;
            foreach (var row in group)
            {
                model.AddConsequence(
                    group.Key,
                    new ConsequenceFunction(index, DecisionVariable.Cost, row.MedianCost, row.Beta));
                index++;
            }
        }
        return model;
    }

    private static EDP CreateEdp(string key)
    {
        var parts = ReSilico.IO.ReSilicoCsv.SplitKey(key);
        string type = parts.Length > 0 ? parts[0] : key;
        int loc = parts.Length > 1 && int.TryParse(parts[1], out var l) ? l : 1;
        int dir = parts.Length > 2 && int.TryParse(parts[2], out var d) ? d : 1;
        return new EDP(type, loc, dir);
    }

    private static List<DistributionParameterSet> GetDefaultDemands() =>
    [
        new() { Name = "PID-1-1", DistributionType = "Lognormal", Param1 = 0.025, Param2 = 0.4 },
        new() { Name = "PID-2-1", DistributionType = "Lognormal", Param1 = 0.030, Param2 = 0.4 },
        new() { Name = "PFA-1-1", DistributionType = "Lognormal", Param1 = 0.50, Param2 = 0.5 },
    ];

    private static List<FragilityParameterSet> GetDefaultFragilities(IEnumerable<DistributionParameterSet> demands) =>
        [.. demands.Select(d => new FragilityParameterSet
        {
            ComponentId = $"C-{d.Name}",
            EdpName = d.Name,
            DamageState = "DS1",
            Median = d.Name.StartsWith("PFA", StringComparison.OrdinalIgnoreCase) ? 0.60 : 0.03,
            Beta = 0.40
        })];

    private static List<LossParameterSet> GetDefaultLosses(IEnumerable<FragilityParameterSet> fragilities) =>
        [.. fragilities.Select(f => new LossParameterSet
        {
            ComponentId = f.ComponentId,
            DamageState = f.DamageState,
            MedianCost = 50_000,
            Beta = 0.50
        })];

    private static double[,] BuildIdentity(int n)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1.0;
        return m;
    }
}
