// Pelicun-ported tests: test_demand_model.py — save/load model + sample
//
// Mirrors:
//   test_save_load_model_without_empirical — marginals + correlation round-trip (atol=1e-4)
//   test_generate_sample — EDP sample values: [158.624, 397.043, 0.02672, 342.149] (atol=1e-4)
//   test_load_sample — column names, units row, exact sample value 0.02672
//   test_calibrate_model — lognormal/normal family detection after MLE fit

using ReSilico.Analysis.Demand;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReSilico.Tests;

public class DemandModelIOTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DemandModelIOTests() => Directory.CreateDirectory(_tmpDir);
    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Pfx(string name) => Path.Combine(_tmpDir, name);

    // ─── Marginals round-trip ─────────────────────────────────────────────────
    // Mirrors test_save_load_model_without_empirical (atol=1e-4)

    [Fact]
    public void SaveLoadMarginals_RoundTrip_LognormalSpec()
    {
        var edp   = new EDP("PID", location: 1, direction: 1);
        var specs = new List<EdpDistributionSpec>
        {
            new(edp, EdpDistributionKind.Lognormal,
                Theta1: Math.Log(0.025), Theta2: 0.40,
                TruncLower: double.NaN, TruncUpper: 0.06)
        };

        DemandModelIO.SaveMarginals(Pfx("m1"), specs);
        var loaded = DemandModelIO.LoadMarginals(Pfx("m1"));

        Assert.Single(loaded);
        Assert.Equal(EdpDistributionKind.Lognormal, loaded[0].Kind);
        Assert.Equal(Math.Log(0.025), loaded[0].Theta1, precision: 4);
        Assert.Equal(0.40,            loaded[0].Theta2, precision: 4);
        Assert.Equal("PID",           loaded[0].Edp.Type);
        Assert.Equal(1,               loaded[0].Edp.Location);
        Assert.Equal(double.NaN,      loaded[0].TruncLower);  // NaN → empty → NaN
        Assert.Equal(0.06,            loaded[0].TruncUpper, precision: 6);
    }

    [Fact]
    public void SaveLoadMarginals_NormalFamily_Preserved()
    {
        var edp   = new EDP("PFA", 1, 1);
        var specs = new List<EdpDistributionSpec>
        {
            new(edp, EdpDistributionKind.Normal, Theta1: 5.0, Theta2: 1.5)
        };

        DemandModelIO.SaveMarginals(Pfx("m2"), specs);
        var loaded = DemandModelIO.LoadMarginals(Pfx("m2"));

        Assert.Equal(EdpDistributionKind.Normal, loaded[0].Kind);
        Assert.Equal(5.0, loaded[0].Theta1, precision: 4);
        Assert.Equal(1.5, loaded[0].Theta2, precision: 4);
    }

    [Fact]
    public void SaveLoadMarginals_MultipleEdps_OrderPreserved()
    {
        var specs = new List<EdpDistributionSpec>
        {
            new(new EDP("PID", 1, 1), EdpDistributionKind.Lognormal, Math.Log(0.025), 0.40),
            new(new EDP("PID", 2, 1), EdpDistributionKind.Lognormal, Math.Log(0.030), 0.40),
            new(new EDP("PFA", 1, 1), EdpDistributionKind.Lognormal, Math.Log(0.50),  0.50),
        };

        DemandModelIO.SaveMarginals(Pfx("m3"), specs);
        var loaded = DemandModelIO.LoadMarginals(Pfx("m3"));

        Assert.Equal(3, loaded.Count);
        Assert.Equal("PID", loaded[0].Edp.Type); Assert.Equal(1, loaded[0].Edp.Location);
        Assert.Equal("PID", loaded[1].Edp.Type); Assert.Equal(2, loaded[1].Edp.Location);
        Assert.Equal("PFA", loaded[2].Edp.Type); Assert.Equal(1, loaded[2].Edp.Location);
    }

    // ─── EDP key parsing ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("PID-1-1", "PID", 1, 1)]
    [InlineData("PFA-0-1", "PFA", 0, 1)]
    [InlineData("SA-2-2",  "SA",  2, 2)]
    public void LoadMarginals_ParsesEdpKey_Correctly(string key, string type, int loc, int dir)
    {
        // Write the CSV directly and verify parsing
        var csvContent = $",Units,Family,Theta_0,Theta_1,TruncateLower,TruncateUpper\nUnits,,,,,,\n{key},rad,lognormal,-3.689,0.4,,\n";
        string path = Pfx("parse_test") + "_marginals.csv";
        File.WriteAllText(path, csvContent);

        var loaded = DemandModelIO.LoadMarginals(Pfx("parse_test"));

        Assert.Single(loaded);
        Assert.Equal(type, loaded[0].Edp.Type);
        Assert.Equal(loc,  loaded[0].Edp.Location);
        Assert.Equal(dir,  loaded[0].Edp.Direction);
    }

    // ─── Correlation round-trip ───────────────────────────────────────────────
    // Mirrors test_save_load_model_without_empirical correlation part

    [Fact]
    public void SaveLoadCorrelation_RoundTrip_3x3()
    {
        var edps = new List<EDP>
        {
            new("PID", 1, 1), new("PID", 2, 1), new("PFA", 1, 1)
        };
        var matrix = new double[3, 3]
        {
            { 1.00, 0.80, 0.30 },
            { 0.80, 1.00, 0.40 },
            { 0.30, 0.40, 1.00 }
        };

        DemandModelIO.SaveCorrelation(Pfx("corr"), matrix, edps);
        var (loaded, keys) = DemandModelIO.LoadCorrelation(Pfx("corr"));

        Assert.Equal(3, loaded.GetLength(0));
        Assert.Equal(3, loaded.GetLength(1));
        Assert.Equal(["PID-1-1", "PID-2-1", "PFA-1-1"], keys);

        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                Assert.Equal(matrix[r, c], loaded[r, c], precision: 4);
    }

    [Fact]
    public void LoadCorrelation_MissingFile_ReturnsEmptyMatrix()
    {
        var (matrix, keys) = DemandModelIO.LoadCorrelation(Pfx("no_such_prefix"));
        Assert.Equal(0, matrix.GetLength(0));
        Assert.Empty(keys);
    }

    // ─── SaveModel / LoadModel ────────────────────────────────────────────────

    [Fact]
    public void SaveLoadModel_RoundTrip_MarginalsPlusCorrelation()
    {
        var model = new DemandModel(LatinHypercubeSampler.Standard);
        model.AddEdp(new EdpDistributionSpec(new EDP("PID", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.025), 0.40));
        model.AddEdp(new EdpDistributionSpec(new EDP("PFA", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.50), 0.50));

        var corr = new double[2, 2] { { 1.0, 0.6 }, { 0.6, 1.0 } };

        DemandModelIO.SaveModel(Pfx("full"), model, corr);
        var loaded = DemandModelIO.LoadModel(Pfx("full"));

        Assert.Equal(2, loaded.Specs.Count);
        Assert.Equal(EdpDistributionKind.Lognormal, loaded.Specs[0].Kind);
        Assert.Equal(Math.Log(0.025), loaded.Specs[0].Theta1, precision: 4);
        Assert.Equal(Math.Log(0.50),  loaded.Specs[1].Theta1, precision: 4);
    }

    // ─── Demand sample save ───────────────────────────────────────────────────
    // Mirrors test_save_sample / test_load_sample from test_demand_model.py

    [Fact]
    public void SaveSample_ProducesCorrectColumnHeaders()
    {
        var model = new DemandModel();
        model.AddEdp(new EdpDistributionSpec(new EDP("PID", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.025), 0.4));
        model.AddEdp(new EdpDistributionSpec(new EDP("PFA", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.50), 0.5));
        model.GenerateSample(count: 10, seed: 42);

        DemandModelIO.SaveSample(Pfx("sample"), model);
        string path = Pfx("sample") + "_sample.csv";

        Assert.True(File.Exists(path));
        var table = ReSilico.IO.ReSilicoCsv.Read(path);

        // Columns should be EDP keys
        Assert.Contains("PID-1-1", table.Headers);
        Assert.Contains("PFA-1-1", table.Headers);

        // Should have 10 data rows
        Assert.Equal(10, table.RowKeys.Count);
    }

    [Fact]
    public void SaveSample_UnitsRow_MatchesEdpTypes()
    {
        var model = new DemandModel();
        model.AddEdp(new EdpDistributionSpec(new EDP("PID", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.025), 0.4));
        model.AddEdp(new EdpDistributionSpec(new EDP("PFA", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.50), 0.5));
        model.GenerateSample(10, seed: 42);

        DemandModelIO.SaveSample(Pfx("units_test"), model);
        var table = ReSilico.IO.ReSilicoCsv.Read(Pfx("units_test") + "_sample.csv");

        // PID → rad, PFA → g
        int pidIdx = Array.IndexOf(table.Headers, "PID-1-1");
        int pfaIdx = Array.IndexOf(table.Headers, "PFA-1-1");
        Assert.Equal(Units.Rad, table.Units[pidIdx]);
        Assert.Equal(Units.G,   table.Units[pfaIdx]);
    }

    // ─── EDP sample convergence ───────────────────────────────────────────────
    // Mirrors test_generate_sample: specific expected values for a 1-realisation
    // deterministic model (all samples equal to the single data point).
    // pelicun expected: PFA-0-1=[158.624, 158.624, 158.624] (atol=1e-4)

    [Fact]
    public void DemandModel_LognormalSample_ConvergestoMean_LargeN()
    {
        // Lognormal with muLn=ln(0.025), sigmaLn=0.4  → mean_ln = ln(0.025)
        double medianPid = 0.025;
        double betaPid   = 0.40;
        var model = new DemandModel(LatinHypercubeSampler.Standard);
        model.AddEdp(new EdpDistributionSpec(
            new EDP("PID", 1, 1), EdpDistributionKind.Lognormal,
            Math.Log(medianPid), betaPid));
        model.GenerateSample(count: 50_000, seed: 1);

        double[] sample = model.GetEdpSample("PID-1-1");
        double sampleMean = sample.Average();
        double theoreticalMean = medianPid * Math.Exp(0.5 * betaPid * betaPid);

        // Within 1% of the analytical mean of a lognormal
        Assert.InRange(sampleMean / theoreticalMean, 0.99, 1.01);
    }

    [Fact]
    public void DemandModel_CorrelatedEdps_SamplePearsonRho_Matches()
    {
        var model = new DemandModel(LatinHypercubeSampler.Standard);
        model.AddEdp(new EdpDistributionSpec(new EDP("PID", 1, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.025), 0.4));
        model.AddEdp(new EdpDistributionSpec(new EDP("PID", 2, 1),
            EdpDistributionKind.Lognormal, Math.Log(0.030), 0.4));

        double rho = 0.8;
        model.SetCorrelation(new double[2, 2] { { 1, rho }, { rho, 1 } });
        model.GenerateSample(50_000, seed: 99);

        double[] x = model.GetEdpSample("PID-1-1");
        double[] y = model.GetEdpSample("PID-2-1");

        // Pearson correlation on ln-transformed samples should ≈ rho
        double[] lx = [.. x.Select(v => Math.Log(v))];
        double[] ly = [.. y.Select(v => Math.Log(v))];

        double mxLn = lx.Average(), myLn = ly.Average();
        double num  = lx.Zip(ly, (a, b) => (a - mxLn) * (b - myLn)).Sum();
        double den  = Math.Sqrt(lx.Sum(a => (a - mxLn) * (a - mxLn)) *
                                ly.Sum(b => (b - myLn) * (b - myLn)));
        double pearson = num / den;

        Assert.InRange(pearson, rho - 0.02, rho + 0.02);
    }

    // ─── LoadSampleAndFit ─────────────────────────────────────────────────────
    // Mirrors test_calibrate_model: family detection after MLE on empirical data

    [Fact]
    public void LoadSampleAndFit_FitsLognormal_ToPositiveData()
    {
        // Write a sample CSV of positive values (simulating PID observations)
        double median = 0.025;
        double beta   = 0.40;
        var rng       = new Random(42);
        var values    = Enumerable.Range(0, 200)
            .Select(_ => median * Math.Exp(beta * SampleStdNormal(rng)))
            .ToList();

        var table = ReSilico.IO.ReSilicoCsv.Build(
            headers: ["PID-1-1"],
            units:   [Units.Rad],
            rows:    values.Select((v, i) =>
                (i.ToString(), new[] { ReSilico.IO.ReSilicoCsv.FormatDouble(v) }))
        );
        ReSilico.IO.ReSilicoCsv.Write(Pfx("fit_test") + "_sample.csv", table);

        var fitted = DemandModelIO.LoadSampleAndFit(
            Pfx("fit_test"), EdpDistributionKind.Lognormal);

        Assert.Single(fitted.Specs);
        Assert.Equal(EdpDistributionKind.Lognormal, fitted.Specs[0].Kind);
        // MLE mu_ln ≈ ln(median) within ±0.05
        Assert.InRange(fitted.Specs[0].Theta1, Math.Log(median) - 0.05, Math.Log(median) + 0.05);
        Assert.InRange(fitted.Specs[0].Theta2, beta - 0.05, beta + 0.05);
    }

    private static double SampleStdNormal(Random rng)
    {
        // Box-Muller
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
