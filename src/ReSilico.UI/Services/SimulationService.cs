using Microsoft.Extensions.Logging;
using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using ReSilico.UI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public class SimulationService : ISimulationService
{
    private readonly ILogger<SimulationService> _logger;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public event EventHandler<double>? ProgressChanged;

    public SimulationService(ILogger<SimulationService> logger)
    {
        _logger = logger;
    }

    public async Task<SimulationResult> RunAsync(SimulationConfiguration config, CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        ProgressChanged?.Invoke(this, 0.0);

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Starting simulation: {N} realizations, seed={Seed}",
                    config.NumberOfRealizations, config.RandomSeed);

                var demands = config.Demands.Count > 0 ? config.Demands : GetDefaultDemands();
                var asset = BuildAsset(demands);

                ISampler sampler = config.SamplingMethod == "MonteCarlo"
                    ? new MonteCarloSampler()
                    : LatinHypercubeSampler.Standard;

                var demandModel = BuildDemandModel(demands, config.CorrelationMatrix, sampler);
                var damageModel = BuildDamageModel(asset);
                var lossModel = BuildLossModel(asset);

                ProgressChanged?.Invoke(this, 0.1);
                cancellationToken.ThrowIfCancellationRequested();

                var result = new SimulationPipeline(asset)
                    .WithDemand(demandModel)
                    .WithDamage(damageModel)
                    .WithLoss(lossModel)
                    .WithSampler(sampler)
                    .Run(config.NumberOfRealizations, config.RandomSeed);

                ProgressChanged?.Invoke(this, 1.0);
                _logger.LogInformation("Simulation complete. Mean cost = {Cost:N0}", result.MeanCost);
                return result;

            }, cancellationToken);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private static Asset BuildAsset(List<DistributionParameterSet> demands)
    {
        var asset = new Asset("ui-asset", "UI Asset");
        foreach (var d in demands)
        {
            var edp = new EDP(d.Name, location: 1);
            var comp = new Component($"C-{d.Name}", edp);
            var ds1 = new DamageState(1, "DS1", "Minor");
            comp.AddDamageState(ds1);
            asset.AddComponent(comp);
        }
        return asset;
    }

    private static DemandModel BuildDemandModel(
        List<DistributionParameterSet> demands,
        double[,] corrMatrix,
        ISampler sampler)
    {
        var model = new DemandModel(sampler);

        foreach (var d in demands)
        {
            var edp = new EDP(d.Name, location: 1);
            var kind = d.DistributionType == "Normal"
                ? EdpDistributionKind.Normal
                : EdpDistributionKind.Lognormal;

            double theta1 = d.DistributionType == "Normal"
                ? d.Param1                    // mean μ
                : Math.Log(d.Param1);         // ln(median)

            model.AddEdp(new EdpDistributionSpec(edp, kind, theta1, d.Param2));
        }

        int n = demands.Count;
        if (corrMatrix.GetLength(0) == n)
            model.SetCorrelation(corrMatrix);
        else
            model.SetCorrelation(BuildIdentity(n));

        return model;
    }

    private static DamageModel BuildDamageModel(Asset asset)
    {
        var model = new DamageModel();
        foreach (var comp in asset.Components)
        {
            var spec = new ComponentFragilitySpec(comp);
            spec.AddFragilityFunction(new FragilityFunction(median: 0.03, beta: 0.4));
            model.Add(spec);
        }
        return model;
    }

    private static LossModel BuildLossModel(Asset asset)
    {
        var model = new LossModel();
        foreach (var comp in asset.Components)
        {
            model.AddConsequence(comp.Id,
                new ConsequenceFunction(
                    damageStateIndex: 1,
                    decisionVariable: DecisionVariable.Cost,
                    medianLoss: 50_000,
                    beta: 0.5));
        }
        return model;
    }

    private static List<DistributionParameterSet> GetDefaultDemands() =>
    [
        new DistributionParameterSet { Name = "PID-1", DistributionType = "Lognormal", Param1 = 0.025, Param2 = 0.4 },
        new DistributionParameterSet { Name = "PID-2", DistributionType = "Lognormal", Param1 = 0.030, Param2 = 0.4 },
        new DistributionParameterSet { Name = "PFA-1", DistributionType = "Lognormal", Param1 = 0.50,  Param2 = 0.5 },
    ];

    private static double[,] BuildIdentity(int n)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1.0;
        return m;
    }
}
