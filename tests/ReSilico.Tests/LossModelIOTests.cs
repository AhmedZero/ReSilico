// Pelicun-ported tests: test_loss_model.py — consequence DB, loss sample IO
//
// Mirrors:
//   test_load_model_parameters   — consequence CSV loading (DS1/DS2 columns)
//   test_aggregate_losses        — total loss is non-negative
//   test_aggregate_losses_thresholds — replacement threshold logic
//   test_decision_variables      — Cost/Time DV routing

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using ReSilico.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReSilico.Tests;

public class LossModelIOTests : IDisposable
{
    private readonly string _tmpDir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LossModelIOTests() => Directory.CreateDirectory(_tmpDir);
    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Pfx(string name) => Path.Combine(_tmpDir, name);

    // ─── Loss database loading ────────────────────────────────────────────────
    // Mirrors test_loss_model.test_load_model_parameters:
    //   Row "cmp.A-Cost" → DS1 medianloss=50000, beta=0.5

    [Fact]
    public void LoadLossDatabase_ParsesDS1_CostRow()
    {
        const string csv =
            ",DV-Unit,DS1-Family,DS1-Theta_0,DS1-Theta_1,DS2-Family,DS2-Theta_0,DS2-Theta_1\n" +
            "Units,,,,,,,\n" +
            "cmp.A-Cost,USD,lognormal,50000,0.5,lognormal,100000,0.5\n" +
            "cmp.A-Time,worker_day,lognormal,30,0.4,,,\n";

        string path = Pfx("loss.csv");
        File.WriteAllText(path, csv);

        var entries = LossModelIO.LoadLossDatabase(path);
        Assert.Equal(3, entries.Count); // DS1+DS2 for Cost, DS1 for Time

        var (componentId, fn) = entries.First(e => e.componentId == "cmp.A" &&
                                        e.fn.DecisionVariable == DecisionVariable.Cost &&
                                        e.fn.DamageStateIndex == 1);
        Assert.Equal(50_000, fn.MedianLoss, precision: 1);
        Assert.Equal(0.5,    fn.Beta,       precision: 4);

        var costDs2 = entries.First(e => e.componentId == "cmp.A" &&
                                        e.fn.DecisionVariable == DecisionVariable.Cost &&
                                        e.fn.DamageStateIndex == 2);
        Assert.Equal(100_000, costDs2.fn.MedianLoss, precision: 1);

        var timeDs1 = entries.First(e => e.componentId == "cmp.A" &&
                                        e.fn.DecisionVariable == DecisionVariable.Time &&
                                        e.fn.DamageStateIndex == 1);
        Assert.Equal(30.0, timeDs1.fn.MedianLoss, precision: 4);
    }

    [Fact]
    public void LoadLossDatabase_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => LossModelIO.LoadLossDatabase("/no/such/file.csv"));
    }

    [Fact]
    public void LoadLossDatabase_CasualtiesDV_ParsedCorrectly()
    {
        const string csv =
            ",DV-Unit,DS1-Family,DS1-Theta_0,DS1-Theta_1\n" +
            "Units,,,,\n" +
            "cmp.Z-Casualties,unitless,lognormal,0.01,0.5\n";

        string path = Pfx("cas.csv");
        File.WriteAllText(path, csv);

        var entries = LossModelIO.LoadLossDatabase(path);
        Assert.Single(entries);
        Assert.Equal(DecisionVariable.Casualties, entries[0].fn.DecisionVariable);
    }

    // ─── Loss DB round-trip ───────────────────────────────────────────────────

    [Fact]
    public void SaveLoadLossDatabase_RoundTrip()
    {
        var entries = new List<(string, ConsequenceFunction)>
        {
            ("cmp.A", new ConsequenceFunction(1, DecisionVariable.Cost,   50_000, 0.5)),
            ("cmp.A", new ConsequenceFunction(2, DecisionVariable.Cost,  100_000, 0.5)),
            ("cmp.A", new ConsequenceFunction(1, DecisionVariable.Time,       30, 0.4)),
            ("cmp.B", new ConsequenceFunction(1, DecisionVariable.Cost,   25_000, 0.3)),
        };

        string path = Pfx("loss_rt.csv");
        LossModelIO.SaveLossDatabase(path, entries);
        var loaded = LossModelIO.LoadLossDatabase(path);

        // Count: cmp.A has Cost-DS1, Cost-DS2, Time-DS1 → 3; cmp.B has Cost-DS1 → 1
        Assert.Equal(4, loaded.Count);

        var (componentId, fn) = loaded.First(e => e.componentId == "cmp.A" &&
                                     e.fn.DecisionVariable == DecisionVariable.Cost &&
                                     e.fn.DamageStateIndex == 1);
        Assert.Equal(50_000, fn.MedianLoss, precision: 0);
    }

    // ─── Loss sample round-trip ───────────────────────────────────────────────

    [Fact]
    public void SaveLoadLossSample_RoundTrip()
    {
        var ids  = new List<string> { "cmp.A", "cmp.B" };
        int n    = 50;
        var orig = new LossSample(ids, n);
        var rng  = new Random(99);
        for (int i = 0; i < n; i++)
        {
            orig.TotalCosts[i] = rng.NextDouble() * 100_000;
            orig.TotalTimes[i] = rng.NextDouble() * 60;
            orig.ComponentCosts[i, 0] = orig.TotalCosts[i] * 0.6;
            orig.ComponentCosts[i, 1] = orig.TotalCosts[i] * 0.4;
        }

        LossModelIO.SaveLossSample(Pfx("loss_samp"), orig);
        var loaded = LossModelIO.LoadLossSample(Pfx("loss_samp"));

        Assert.Equal(n, loaded.Count);
        Assert.Equal(2, loaded.ComponentIds.Count);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(orig.TotalCosts[i], loaded.TotalCosts[i], precision: 2);
            Assert.Equal(orig.TotalTimes[i], loaded.TotalTimes[i], precision: 2);
            Assert.Equal(orig.ComponentCosts[i, 0], loaded.ComponentCosts[i, 0], precision: 2);
            Assert.Equal(orig.ComponentCosts[i, 1], loaded.ComponentCosts[i, 1], precision: 2);
        }
    }

    [Fact]
    public void SaveLossSample_HeaderContains_CostTotal_And_PerComponent()
    {
        var ids  = new List<string> { "cmp.1", "cmp.2" };
        var samp = new LossSample(ids, 5);
        for (int i = 0; i < 5; i++)
        {
            samp.TotalCosts[i] = i * 1000.0;
            samp.TotalTimes[i] = i * 2.0;
        }

        LossModelIO.SaveLossSample(Pfx("header_test"), samp);
        var table = ReSilicoCsv.Read(Pfx("header_test") + "_sample.csv");

        Assert.Contains("Cost-Total",  table.Headers);
        Assert.Contains("Time-Total",  table.Headers);
        Assert.Contains("Cost-cmp.1",  table.Headers);
        Assert.Contains("Cost-cmp.2",  table.Headers);
    }

    // ─── Integration: full pipeline + IO ──────────────────────────────────────
    // Mirrors test_loss_model.test_aggregate_losses_combination:
    //   total loss should be positive for a reasonable demand+fragility setup

    [Fact]
    public void FullPipeline_SaveResults_FilesExist()
    {
        var (result, demandModel) = RunMinimalPipeline(n: 500, seed: 1);

        string dir = Pfx("assessment_out");
        AssessmentIO.SaveResults(dir, result, demandModel);

        Assert.True(File.Exists(Path.Combine(dir, "demand_marginals.csv")));
        Assert.True(File.Exists(Path.Combine(dir, "damage_sample.csv")));
        Assert.True(File.Exists(Path.Combine(dir, "loss_sample.csv")));
    }

    [Fact]
    public void FullPipeline_LoadResults_ReconstructsLossSample()
    {
        var (result, demandModel) = RunMinimalPipeline(n: 300, seed: 42);

        string dir = Pfx("load_rt");
        AssessmentIO.SaveResults(dir, result, demandModel);

        var snapshot = AssessmentIO.LoadResults(dir);
        Assert.NotNull(snapshot.LossSample);
        Assert.Equal(300, snapshot.LossSample!.Count);
    }

    [Fact]
    public void FullPipeline_MeanTotalLoss_IsPositive()
    {
        // Mirrors test_aggregate_losses: expected loss > 0
        var (result, _) = RunMinimalPipeline(n: 1000, seed: 7);
        Assert.True(result.MeanCost > 0.0);
    }

    [Fact]
    public void FullPipeline_Percentiles_AreOrdered()
    {
        // Mirrors loss model threshold / ordering checks
        var (result, _) = RunMinimalPipeline(n: 1000, seed: 8);
        double p5  = result.CostAtPercentile(0.05);
        double p50 = result.CostAtPercentile(0.50);
        double p95 = result.CostAtPercentile(0.95);
        Assert.True(p5 <= p50);
        Assert.True(p50 <= p95);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static (SimulationResult result, DemandModel demandModel) RunMinimalPipeline(
        int n, int seed)
    {
        var edp  = new EDP("PID", 1, 1);
        var comp = new Component("cmp.A", edp);
        comp.AddDamageState(new DamageState(1, "DS1"));

        var asset = new Asset("test", "Test");
        asset.AddComponent(comp);

        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(0.025, 0.4));
        var dmgModel = new DamageModel();
        dmgModel.Add(spec);

        var lossModel = new LossModel();
        lossModel.AddConsequence("cmp.A",
            new ConsequenceFunction(1, DecisionVariable.Cost, 50_000, 0.5));

        var demandModel = new DemandModel();
        demandModel.AddEdp(new EdpDistributionSpec(
            edp, EdpDistributionKind.Lognormal, Math.Log(0.025), 0.4));

        var result = new SimulationPipeline(asset)
            .WithDemand(demandModel)
            .WithDamage(dmgModel)
            .WithLoss(lossModel)
            .Run(n, seed);

        return (result, demandModel);
    }
}
