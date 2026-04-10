// Pelicun-ported tests: test_damage_model.py — fragility DB, damage sample IO
//
// Mirrors:
//   test_load_model_parameters   — fragility CSV loading (LS1/LS2 columns)
//   test__obtain_ds_sample       — demand=5 m/s², Theta_0=1.0 → DS1, Theta_0=10 → DS0
//   test__evaluate_damage_state  — progressive damage state assignment
//   test__handle_operation       — arithmetic helpers (+, -, ×, ÷)

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using ReSilico.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReSilico.Tests;

public class DamageModelIOTests : IDisposable
{
    private readonly string _tmpDir =
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DamageModelIOTests() => Directory.CreateDirectory(_tmpDir);
    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private string Pfx(string name) => Path.Combine(_tmpDir, name);

    // ─── Fragility database loading ───────────────────────────────────────────
    // Mirrors test_load_model_parameters:
    //   component.A → LS1=0.02, LS2=0.04, LS3=0.08 (unitless / lognormal)
    //   component.B → LS1=0.2g, LS2=0.4g  (with unit column)

    [Fact]
    public void LoadFragilityDatabase_ParsesLS1_AndLS2_Columns()
    {
        const string csv =
            ",Demand-Type,Demand-Unit,LS1-Family,LS1-Theta_0,LS1-Theta_1,LS2-Family,LS2-Theta_0,LS2-Theta_1\n" +
            "Units,,,,,,,\n" +
            "cmp.A,Peak Interstory Drift Ratio,unitless,lognormal,0.02,0.40,lognormal,0.04,0.40\n" +
            "cmp.B,Peak Floor Acceleration,unitless,lognormal,0.50,0.30,,,,\n";

        string dbPath = Pfx("fragility.csv");
        File.WriteAllText(dbPath, csv);

        var asset = BuildAsset(["cmp.A", "cmp.B"], new EDP("PID", 1, 1));
        var specs  = DamageModelIO.LoadFragilityDatabase(dbPath, asset);

        Assert.Equal(2, specs.Count);

        var specA = specs.First(s => s.Component.Id == "cmp.A");
        Assert.Equal(2, specA.NumberOfLimitStates);
        Assert.Equal(0.02, specA.FragilityFunctions[0].Median, precision: 6);
        Assert.Equal(0.40, specA.FragilityFunctions[0].Beta,   precision: 6);
        Assert.Equal(0.04, specA.FragilityFunctions[1].Median, precision: 6);

        var specB = specs.First(s => s.Component.Id == "cmp.B");
        Assert.Equal(1, specB.NumberOfLimitStates);
        Assert.Equal(0.50, specB.FragilityFunctions[0].Median, precision: 6);
    }

    [Fact]
    public void LoadFragilityDatabase_OnlyLoadsMatchingComponents()
    {
        const string csv =
            ",Demand-Type,Demand-Unit,LS1-Family,LS1-Theta_0,LS1-Theta_1\n" +
            "Units,,,,,\n" +
            "cmp.A,PID,unitless,lognormal,0.02,0.4\n" +
            "cmp.B,PID,unitless,lognormal,0.05,0.4\n" +
            "cmp.NOTINASSET,PID,unitless,lognormal,0.10,0.4\n";

        string dbPath = Pfx("partial.csv");
        File.WriteAllText(dbPath, csv);

        // Asset only has cmp.A — cmp.B and cmp.NOTINASSET should be ignored
        var asset = BuildAsset(["cmp.A"], new EDP("PID", 1, 1));
        var specs  = DamageModelIO.LoadFragilityDatabase(dbPath, asset);

        Assert.Single(specs);
        Assert.Equal("cmp.A", specs[0].Component.Id);
    }

    [Fact]
    public void LoadFragilityDatabase_MissingFile_Throws()
    {
        var asset = BuildAsset(["cmp.X"], new EDP("PID", 1, 1));
        Assert.Throws<FileNotFoundException>(
            () => DamageModelIO.LoadFragilityDatabase("/no/file.csv", asset));
    }

    // ─── Fragility DB save → reload round-trip ────────────────────────────────

    [Fact]
    public void SaveLoadFragilityDatabase_RoundTrip()
    {
        var edp    = new EDP("PID", 1, 1);
        var asset  = BuildAsset(["cmp.1", "cmp.2"], edp);
        var specs  = new List<ComponentFragilitySpec>();

        foreach (var comp in asset.Components)
        {
            var spec = new ComponentFragilitySpec(comp);
            spec.AddFragilityFunction(new FragilityFunction(0.02, 0.4, "DS1"));
            spec.AddFragilityFunction(new FragilityFunction(0.04, 0.4, "DS2"));
            specs.Add(spec);
        }

        string dbPath = Pfx("ff_roundtrip.csv");
        DamageModelIO.SaveFragilityDatabase(dbPath, specs);

        var loaded = DamageModelIO.LoadFragilityDatabase(dbPath, asset);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(2, loaded[0].NumberOfLimitStates);
        Assert.Equal(0.02, loaded[0].FragilityFunctions[0].Median, precision: 6);
        Assert.Equal(0.04, loaded[0].FragilityFunctions[1].Median, precision: 6);
    }

    // ─── Damage sample round-trip ─────────────────────────────────────────────

    [Fact]
    public void SaveLoadDamageSample_RoundTrip()
    {
        // Mirrors test_damage_model.test_save_sample / test_load_sample
        var ids   = new List<string> { "cmp.A", "cmp.B", "cmp.C" };
        int n     = 100;
        var original = new DamageSample(ids, n);
        var rng   = new Random(17);
        for (int i = 0; i < n; i++)
            for (int c = 0; c < 3; c++)
                original.DamageStates[i, c] = rng.Next(0, 4);

        DamageModelIO.SaveDamageSample(Pfx("ds"), original);
        var loaded = DamageModelIO.LoadDamageSample(Pfx("ds"));

        Assert.Equal(n, loaded.Count);
        Assert.Equal(3, loaded.NumberOfComponents);
        Assert.Equal(ids, [.. loaded.ComponentIds]);

        for (int i = 0; i < n; i++)
            for (int c = 0; c < 3; c++)
                Assert.Equal(original.DamageStates[i, c], loaded.DamageStates[i, c]);
    }

    // ─── Damage state assignment (test__obtain_ds_sample) ────────────────────
    // demand=5.0, Theta_0=1.0 → fragility ≈ 1.0 → DS1
    // demand=5.0, Theta_0=10.0 → fragility ≈ 0.0 → DS0

    [Fact]
    public void FragilityFunction_VeryHighDemand_NearlyAlwaysExceeded()
    {
        // Theta_0=1.0 (median), demand=5.0 → P(DS≥1|EDP=5) >> 0.99
        var ff = new FragilityFunction(median: 1.0, beta: 0.4);
        double prob = ff.Evaluate(edp: 5.0);
        Assert.InRange(prob, 0.99, 1.00);
    }

    [Fact]
    public void FragilityFunction_VeryLowDemandVsHighCapacity_NearlyNeverExceeded()
    {
        // Theta_0=10.0 (median), demand=5.0 → P(DS≥1|EDP=5) ≈ 0.13
        var ff   = new FragilityFunction(median: 10.0, beta: 0.4);
        double p = ff.Evaluate(edp: 5.0);
        Assert.InRange(p, 0.0, 0.20);   // well below 0.5
    }

    [Fact]
    public void FragilityFunction_AtMedian_IsBetweenPoint49AndPoint51()
    {
        // Mirrors test_damage_model fragility at median = exactly 0.5
        foreach (double median in new[] { 0.01, 0.03, 0.05, 1.96 })
        {
            var ff = new FragilityFunction(median, beta: 0.4);
            Assert.InRange(ff.Evaluate(median), 0.49, 0.51);
        }
    }

    // ─── Progressive damage state assignment ──────────────────────────────────
    // Mirrors test__evaluate_damage_state:
    //   demand=1.0, capacity LS1=2.0 → DS0
    //   demand=3.0, capacity LS1=2.0, LS2=4.0 → DS1
    //   demand=5.0, capacity LS1=2.0, LS2=4.0 → DS2

    [Fact]
    public void DamageModel_ProgressiveDamageStates_AssignedCorrectly()
    {
        // Build a 1-component asset with 2 limit states
        var edp  = new EDP("PID", 1, 1);
        var comp = new Component("cmp.A", edp);
        comp.AddDamageState(new DamageState(1, "DS1"));
        comp.AddDamageState(new DamageState(2, "DS2"));

        var asset = new Asset("test", "Test");
        asset.AddComponent(comp);

        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(median: 0.01, beta: 0.4)); // LS1
        spec.AddFragilityFunction(new FragilityFunction(median: 0.03, beta: 0.4)); // LS2

        var dmgModel = new DamageModel();
        dmgModel.Add(spec);

        // Low demand → DS0 (no damage)
        var demandLow = BuildDemandModel(edp, edpValue: 0.001);
        demandLow.GenerateSample(1000, seed: 1);
        var damageLow = dmgModel.Evaluate(
            edpKey => demandLow.GetEdpSample(edpKey),
            count: demandLow.SampleCount,
            seed: 2);
        double ds0Prob = damageLow.GetDamageStateProbabilities("cmp.A", 2)[0];
        Assert.InRange(ds0Prob, 0.90, 1.00); // should be DS0 almost always

        // High demand → DS2 almost always
        var demandHigh = BuildDemandModel(edp, edpValue: 0.20);
        demandHigh.GenerateSample(1000, seed: 3);
        var damageHigh = dmgModel.Evaluate(
            edpKey => demandHigh.GetEdpSample(edpKey),
            count: demandHigh.SampleCount,
            seed: 4);
        double ds2Prob = damageHigh.GetDamageStateProbabilities("cmp.A", 2)[2];
        Assert.InRange(ds2Prob, 0.90, 1.00); // should be DS2 almost always
    }

    [Fact]
    public void DamageModel_Evaluate_NeverExceedsMaxDamageState()
    {
        var edp  = new EDP("PID", 1, 1);
        var comp = new Component("cmp.X", edp);
        comp.AddDamageState(new DamageState(1, "DS1"));
        var asset = new Asset("test2", "Test2");
        asset.AddComponent(comp);

        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(0.01, 0.3));
        var model = new DamageModel();
        model.Add(spec);

        var demand = BuildDemandModel(edp, 1.0);
        demand.GenerateSample(500, seed: 5);
        var sample = model.Evaluate(
            edpKey => demand.GetEdpSample(edpKey),
            count: demand.SampleCount,
            seed: 6);

        int maxDs = (int)sample.DamageStates.Cast<int>().Max();
        Assert.InRange(maxDs, 0, 1);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Asset BuildAsset(IEnumerable<string> ids, EDP edp)
    {
        var asset = new Asset("test-asset", "Test Asset");
        foreach (var id in ids)
        {
            var comp = new Component(id, edp);
            comp.AddDamageState(new DamageState(1, "DS1"));
            asset.AddComponent(comp);
        }
        return asset;
    }

    private static DemandModel BuildDemandModel(EDP edp, double edpValue)
    {
        // Build a near-deterministic demand model (very low beta → almost constant)
        var model = new DemandModel();
        model.AddEdp(new EdpDistributionSpec(
            edp, EdpDistributionKind.Lognormal,
            Theta1: Math.Log(edpValue),
            Theta2: 0.001)); // near-zero dispersion → deterministic
        return model;
    }
}
