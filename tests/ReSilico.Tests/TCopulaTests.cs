// ReSilico – Probabilistic Damage and Loss Assessment Engine
// TCopulaTests: correctness, tail dependence, limiting behaviour, integration

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Copulas;
using ReSilico.Core.Distributions;
using ReSilico.Core.RandomVariables;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using System;
using System.Linq;

namespace ReSilico.Tests;

public class TCopulaTests
{
    private const int N    = 50_000;
    private const int Seed = 99;

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>2×2 correlation matrix with given rho.</summary>
    private static double[,] Rho2(double rho) =>
        new double[,] { { 1.0, rho }, { rho, 1.0 } };

    /// <summary>Generate N independent U(0,1) pairs using LHS.</summary>
    private static double[,] IndependentUniforms(int count, int dim, int seed)
    {
        var sampler = LatinHypercubeSampler.Standard;
        return sampler.GenerateUniform(count, dim, seed);
    }

    /// <summary>Pearson correlation of two arrays.</summary>
    private static double Pearson(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double num = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
        double dx  = Math.Sqrt(x.Sum(a => (a - mx) * (a - mx)));
        double dy  = Math.Sqrt(y.Sum(b => (b - my) * (b - my)));
        return num / (dx * dy);
    }

    /// <summary>Empirical P(U1 > q AND U2 > q) from copula output.</summary>
    private static double JointExceedance(double[,] u, double q)
    {
        int n = u.GetLength(0);
        int count = 0;
        for (int i = 0; i < n; i++)
            if (u[i, 0] > q && u[i, 1] > q) count++;
        return count / (double)n;
    }

    // ─── ICopula type hierarchy ────────────────────────────────────────────────

    [Fact]
    public void GaussianCopula_ImplementsICopula()
    {
        var c = new GaussianCopula(Rho2(0.5));
        Assert.IsAssignableFrom<ICopula>(c);
        Assert.IsAssignableFrom<CopulaBase>(c);
    }

    [Fact]
    public void TCopula_ImplementsICopula()
    {
        var c = new TCopula(5.0, Rho2(0.5));
        Assert.IsAssignableFrom<ICopula>(c);
        Assert.IsAssignableFrom<CopulaBase>(c);
    }

    [Fact]
    public void TCopula_InvalidDf_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TCopula(2.0, Rho2(0.5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TCopula(1.0, Rho2(0.5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TCopula(-1.0, Rho2(0.5)));
    }

    [Fact]
    public void TCopula_ExposesDegreesOfFreedom()
    {
        var c = new TCopula(7.0, Rho2(0.5));
        Assert.Equal(7.0, c.DegreesOfFreedom);
    }

    // ─── 1. Marginal uniformity ────────────────────────────────────────────────
    // After applying the copula, each marginal column must still look uniform:
    //   • All values ∈ (0, 1)
    //   • Mean ≈ 0.5  (tolerance 0.01 with N=50k)

    [Fact]
    public void GaussianCopula_Marginals_AreUniform()
    {
        var u = IndependentUniforms(N, 2, Seed);
        new GaussianCopula(Rho2(0.5)).ApplyCorrelation(u);

        for (int j = 0; j < 2; j++)
        {
            var col = Enumerable.Range(0, N).Select(i => u[i, j]).ToArray();
            Assert.All(col, v => Assert.InRange(v, 0.0, 1.0));
            Assert.InRange(col.Average(), 0.49, 0.51);
        }
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    [InlineData(30.0)]
    public void TCopula_Marginals_AreUniform(double nu)
    {
        var u = IndependentUniforms(N, 2, Seed);
        new TCopula(nu, Rho2(0.5), Seed).ApplyCorrelation(u);

        for (int j = 0; j < 2; j++)
        {
            var col = Enumerable.Range(0, N).Select(i => u[i, j]).ToArray();
            Assert.All(col, v => Assert.InRange(v, 0.0, 1.0));
            Assert.InRange(col.Average(), 0.48, 0.52);
        }
    }

    // ─── 2. Correlation preservation ──────────────────────────────────────────
    // Transform copula output to standard normal via Φ⁻¹ and measure Pearson.
    // For the Gaussian copula this exactly equals the target ρ.
    // For t-copula we test that the rank-based correlation is preserved
    // (which manifests as similar Pearson in the normal-transformed space).

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(-0.6)]
    public void GaussianCopula_NormalSpaceCorrelation_MatchesTarget(double rho)
    {
        var u = IndependentUniforms(N, 2, Seed);
        new GaussianCopula(Rho2(rho)).ApplyCorrelation(u);

        // Transform back to normal space
        double[] z1 = [.. Enumerable.Range(0, N).Select(i => NormalDistribution.StandardInverseCDF(u[i, 0]))];
        double[] z2 = [.. Enumerable.Range(0, N).Select(i => NormalDistribution.StandardInverseCDF(u[i, 1]))];

        double r = Pearson(z1, z2);
        Assert.InRange(r, rho - 0.02, rho + 0.02);
    }

    [Theory]
    [InlineData(5.0,  0.5)]
    [InlineData(10.0, 0.8)]
    [InlineData(30.0, 0.5)]
    public void TCopula_NormalSpaceCorrelation_CloseToTarget(double nu, double rho)
    {
        var u = IndependentUniforms(N, 2, Seed);
        new TCopula(nu, Rho2(rho), Seed).ApplyCorrelation(u);

        // Normal-space correlation is not exactly rho for t-copula,
        // but should be in the right ballpark (within ±0.12 for ν≥5).
        double[] z1 = [.. Enumerable.Range(0, N).Select(i => NormalDistribution.StandardInverseCDF(u[i, 0]))];
        double[] z2 = [.. Enumerable.Range(0, N).Select(i => NormalDistribution.StandardInverseCDF(u[i, 1]))];

        double r = Pearson(z1, z2);
        Assert.InRange(r, rho - 0.12, rho + 0.12);
    }

    // ─── 3. Tail dependence (CRITICAL) ────────────────────────────────────────
    // P(U1 > q AND U2 > q) must be materially larger for t-copula.
    // With ρ=0.5, ν=3, theoretical λ_U ≈ 0.31, so the t-copula produces
    // far more joint exceedances near the tail than the Gaussian copula.

    [Theory]
    [InlineData(0.90, 0.5, 1.20)]   // q=0.90 is moderate tail — expect ≥20% excess
    [InlineData(0.95, 0.5, 1.50)]   // q=0.95 is deep tail — expect ≥50% excess
    [InlineData(0.95, 0.3, 1.50)]
    public void TCopula_TailExceedance_IsHigherThanGaussian(double q, double rho, double minFactor)
    {
        var uGauss = IndependentUniforms(N, 2, Seed);
        new GaussianCopula(Rho2(rho)).ApplyCorrelation(uGauss);

        var uT = IndependentUniforms(N, 2, Seed);
        new TCopula(degreesOfFreedom: 3.0, Rho2(rho), Seed).ApplyCorrelation(uT);

        double pGauss = JointExceedance(uGauss, q);
        double pT     = JointExceedance(uT, q);

        // The t-copula should produce materially more joint exceedances than the Gaussian.
        Assert.True(pT > pGauss * minFactor,
            $"Expected t-copula P={pT:F4} > {minFactor}× Gaussian P={pGauss:F4} at q={q}, ρ={rho}");
    }

    // ─── 4. Theoretical tail dependence coefficient ───────────────────────────

    [Theory]
    [InlineData(3.0,  0.5,  0.25, 0.40)]   // λ_U ≈ 0.31 for ν=3, ρ=0.5
    [InlineData(5.0,  0.5,  0.10, 0.30)]   // λ_U ≈ 0.18 for ν=5, ρ=0.5
    [InlineData(3.0,  0.8,  0.45, 0.65)]   // higher ρ → higher tail dep.
    public void TCopula_TailDependenceCoefficient_InExpectedRange(
        double nu, double rho, double lambdaLo, double lambdaHi)
    {
        var c = new TCopula(nu, Rho2(rho));
        double lambda = c.TailDependence(rho);
        Assert.InRange(lambda, lambdaLo, lambdaHi);
    }

    [Fact]
    public void GaussianCopula_HasZeroTailDependence_By_LargeNuLimit()
    {
        // As ν → ∞, t-copula λ_U → 0 (matching Gaussian).
        var c = new TCopula(500.0, Rho2(0.5));
        double lambda = c.TailDependence(0.5);
        Assert.InRange(lambda, 0.0, 0.02);
    }

    // ─── 5. Limiting case: large ν ≈ Gaussian ─────────────────────────────────
    // For ν = 500, the t-copula produces virtually the same joint tail
    // probability as the Gaussian copula.

    [Fact]
    public void TCopula_LargeNu_ConvergesToGaussian_TailExceedance()
    {
        double rho = 0.5, q = 0.95;

        var uGauss = IndependentUniforms(N, 2, Seed);
        new GaussianCopula(Rho2(rho)).ApplyCorrelation(uGauss);

        var uT = IndependentUniforms(N, 2, Seed);
        new TCopula(degreesOfFreedom: 500.0, Rho2(rho), Seed).ApplyCorrelation(uT);

        double pGauss = JointExceedance(uGauss, q);
        double pT     = JointExceedance(uT, q);

        // Large-ν t-copula should be indistinguishable from Gaussian at this precision.
        Assert.InRange(Math.Abs(pT - pGauss) / (pGauss + 1e-12), 0.0, 0.30);
    }

    // ─── 6. Reproducibility ───────────────────────────────────────────────────

    [Fact]
    public void TCopula_SameSeed_ProducesSameOutput()
    {
        var rho2 = Rho2(0.5);

        var u1 = IndependentUniforms(100, 2, Seed);
        new TCopula(5.0, rho2, Seed).ApplyCorrelation(u1);

        var u2 = IndependentUniforms(100, 2, Seed);
        new TCopula(5.0, rho2, Seed).ApplyCorrelation(u2);

        for (int i = 0; i < 100; i++)
            for (int j = 0; j < 2; j++)
                Assert.Equal(u1[i, j], u2[i, j], precision: 12);
    }

    // ─── 7. High-dimensional correlation preservation ─────────────────────────

    [Fact]
    public void TCopula_5D_Marginals_AreUniform()
    {
        int dim = 5;
        double[,] rho5 = new double[dim, dim];
        for (int i = 0; i < dim; i++) rho5[i, i] = 1.0;
        for (int i = 0; i < dim; i++)
            for (int j = i + 1; j < dim; j++)
            {
                rho5[i, j] = 0.4;
                rho5[j, i] = 0.4;
            }

        var u = IndependentUniforms(N, dim, Seed);
        new TCopula(5.0, rho5, Seed).ApplyCorrelation(u);

        for (int j = 0; j < dim; j++)
        {
            double mean = Enumerable.Range(0, N).Average(i => u[i, j]);
            Assert.InRange(mean, 0.47, 0.53);
        }
    }

    // ─── 8. RandomVariableSet backward compatibility ──────────────────────────

    [Fact]
    public void RandomVariableSet_OldConstructor_DefaultsToGaussianCopula()
    {
        var rv1 = new RandomVariable("X", new NormalDistribution(0, 1));
        var rv2 = new RandomVariable("Y", new NormalDistribution(0, 1));
        var rhs = Rho2(0.5);

        var set = new RandomVariableSet("test", [rv1, rv2], rhs);

        Assert.IsType<GaussianCopula>(set.Copula);
    }

    [Fact]
    public void RandomVariableSet_SwitchesToTCopula_ViaProperty()
    {
        var rv1 = new RandomVariable("X", new NormalDistribution(0, 1));
        var rv2 = new RandomVariable("Y", new NormalDistribution(0, 1));
        var rho = Rho2(0.5);

        var set = new RandomVariableSet("test", [rv1, rv2], rho);
        Assert.IsType<GaussianCopula>(set.Copula);

        set.Copula = new TCopula(5.0, rho);
        Assert.IsType<TCopula>(set.Copula);
    }

    [Fact]
    public void RandomVariableSet_NewConstructor_AcceptsICopula()
    {
        var rv1 = new RandomVariable("A", new NormalDistribution(0, 1));
        var rv2 = new RandomVariable("B", new NormalDistribution(0, 1));
        ICopula tc = new TCopula(4.0, Rho2(0.7));

        var set = new RandomVariableSet("copula-set", [rv1, rv2], tc);
        Assert.IsType<TCopula>(set.Copula);
    }

    [Fact]
    public void RandomVariableSet_TCopula_ProducesCorrelatedSamples()
    {
        // End-to-end: registry uses t-copula, resulting samples are correlated.
        double targetRho = 0.7;
        var rho = Rho2(targetRho);

        var rv1 = new RandomVariable("X", new NormalDistribution(0, 1));
        var rv2 = new RandomVariable("Y", new NormalDistribution(0, 1));

        var set = new RandomVariableSet("t-set", [rv1, rv2],
            new TCopula(10.0, rho, Seed));

        var registry = new RandomVariableRegistry(LatinHypercubeSampler.Standard);
        registry.RegisterSet(set);
        registry.GenerateSample(N, Seed);

        double[] x = registry.GetSample("X");
        double[] y = registry.GetSample("Y");
        double r = Pearson(x, y);

        // Target ρ=0.7; allowing wider tolerance for t(10) vs Gaussian
        Assert.InRange(r, targetRho - 0.15, targetRho + 0.15);
    }

    // ─── 9. Dimension mismatch guard ──────────────────────────────────────────

    [Fact]
    public void CopulaBase_NonSquareMatrix_Throws()
    {
        var badMatrix = new double[2, 3]; // not square
        Assert.Throws<ArgumentException>(() => new GaussianCopula(badMatrix));
        Assert.Throws<ArgumentException>(() => new TCopula(5.0, badMatrix));
    }

    [Fact]
    public void TCopula_SpearmanCorrelation_IsReasonable()
    {
        int N = 50_000;
        int seed = 42;
        double rho = 0.5;

        var u = LatinHypercubeSampler.Standard.GenerateUniform(N, 2, seed);
        new TCopula(5.0, new double[,] { { 1, rho }, { rho, 1 } }, seed)
            .ApplyCorrelation(u);

        double[] x = [.. Enumerable.Range(0, N).Select(i => u[i, 0])];
        double[] y = [.. Enumerable.Range(0, N).Select(i => u[i, 1])];

        double spearman = Spearman(x, y);

        Assert.InRange(spearman, 0.3, 0.7);
    }

    // Helper
    private static double Spearman(double[] x, double[] y)
    {
        double[] rx = Rank(x);
        double[] ry = Rank(y);
        return Pearson(rx, ry);
    }

    private static double[] Rank(double[] values)
    {
        var sorted = values
            .Select((v, i) => (Value: v, Index: i))
            .OrderBy(t => t.Value)
            .ToArray();

        double[] ranks = new double[values.Length];
        for (int i = 0; i < sorted.Length; i++)
            ranks[sorted[i].Index] = i + 1;

        return ranks;
    }

    [Fact]
    public void TCopula_ExtremeTailDependence_StrongerThanGaussian()
    {
        int N = 80_000;
        int seed = 123;
        double rho = 0.5;
        double q = 0.99;

        var uGauss = LatinHypercubeSampler.Standard.GenerateUniform(N, 2, seed);
        new GaussianCopula(new double[,] { { 1, rho }, { rho, 1 } })
            .ApplyCorrelation(uGauss);

        var uT = LatinHypercubeSampler.Standard.GenerateUniform(N, 2, seed);
        new TCopula(3.0, new double[,] { { 1, rho }, { rho, 1 } }, seed)
            .ApplyCorrelation(uT);

        double pGauss = JointExceedance(uGauss, q);
        double pT = JointExceedance(uT, q);

        Assert.True(pT > pGauss * 1.8,
            $"Extreme tail not strong enough: t={pT}, gauss={pGauss}");
    }

    [Fact]
    public void TCopula_Performance_ShouldBeReasonable()
    {
        int N = 100_000;
        int seed = 42;
        double[,] rho = { { 1, 0.5 }, { 0.5, 1 } };

        var u = LatinHypercubeSampler.Standard.GenerateUniform(N, 2, seed);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        new TCopula(5.0, rho, seed).ApplyCorrelation(u);

        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"Too slow: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TCopula_LargeN_ShouldRemainStable()
    {
        int N = 200_000;
        int seed = 77;

        var u = LatinHypercubeSampler.Standard.GenerateUniform(N, 2, seed);

        new TCopula(5.0, new double[,] { { 1, 0.6 }, { 0.6, 1 } }, seed)
            .ApplyCorrelation(u);

        // Check no NaN / Inf
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                Assert.False(double.IsNaN(u[i, j]));
                Assert.False(double.IsInfinity(u[i, j]));
            }
        }

        // Still uniform-ish
        double mean = Enumerable.Range(0, N).Average(i => u[i, 0]);
        Assert.InRange(mean, 0.48, 0.52);
    }

    [Fact]
    public void RandomVariableSet_DimensionMismatch_Throws()
    {
        var rv1 = new RandomVariable("X", new NormalDistribution(0, 1));
        var rv2 = new RandomVariable("Y", new NormalDistribution(0, 1));

        var set = new RandomVariableSet("test", [rv1, rv2],
            new TCopula(5.0, new double[,] { { 1, 0.5 }, { 0.5, 1 } }));

        // Wrong dimension: 3 columns instead of 2
        var u = new double[100, 3];

        Assert.Throws<ArgumentException>(() =>
            set.ApplyCorrelation(u));
    }
    private static double[,] BuildCorrelationMatrix(int stories)
    {
        int nPID = stories;
        int nPFA = stories + 1;
        int n = nPID + nPFA;
        var rho = new double[n, n];
        for (int i = 0; i < n; i++) rho[i, i] = 1.0;

        // Within PID (indices 0..5): exponential spatial correlation
        for (int i = 0; i < nPID; i++)
            for (int j = 0; j < nPID; j++)
                if (i != j) rho[i, j] = Math.Exp(-0.40 * Math.Abs(i - j));

        // Within PFA (indices 6..12)
        for (int i = 0; i < nPFA; i++)
            for (int j = 0; j < nPFA; j++)
                if (i != j) rho[nPID + i, nPID + j] = Math.Exp(-0.35 * Math.Abs(i - j));

        // Cross-type PID↔PFA = 0.40 (moderate)
        for (int i = 0; i < nPID; i++)
            for (int j = 0; j < nPFA; j++)
            {
                rho[i, nPID + j] = 0.40;
                rho[nPID + j, i] = 0.40;
            }

        return rho;
    }
    private static (SimulationPipeline pipeline, double[,] corr)
    BuildAdvancedPipeline(bool useTCopula, double[,]? corrOverride = null, int df = 5)
    {
        const int Stories = 6;

        var asset = new Asset("SMF-6", "6-Story Steel Office", HazardType.Earthquake)
        {
            ReplacementCost = 12_000_000,
            FloorArea = 5_000,
            NumberOfStories = Stories,
            OccupancyType = "Office",
        };

        var pidEdps = Enumerable.Range(1, Stories).Select(s => new EDP("PID", s, 1, "rad")).ToArray();
        var pfaEdps = Enumerable.Range(0, Stories + 1).Select(s => new EDP("PFA", s, 1, "g")).ToArray();

        // --- Demand model
        var demandModel = new DemandModel();

        double[] pidMedians = [0.003, 0.005, 0.007, 0.008, 0.007, 0.005];
        double[] pidBetas = [0.40, 0.38, 0.35, 0.35, 0.38, 0.40];

        for (int s = 0; s < Stories; s++)
            demandModel.AddEdp(new EdpDistributionSpec(
                pidEdps[s], EdpDistributionKind.Lognormal,
                Math.Log(pidMedians[s]), pidBetas[s], 0.0));

        double[] pfaMedians = [0.20, 0.28, 0.36, 0.44, 0.50, 0.54, 0.58];
        double[] pfaBetas = [0.30, 0.32, 0.33, 0.33, 0.32, 0.32, 0.30];

        for (int s = 0; s <= Stories; s++)
            demandModel.AddEdp(new EdpDistributionSpec(
                pfaEdps[s], EdpDistributionKind.Lognormal,
                Math.Log(pfaMedians[s]), pfaBetas[s], 0.0));

        var corr = corrOverride ?? BuildCorrelationMatrix(Stories);

        // 🔥 KEY PART: switch copula
        if (useTCopula)
            demandModel.SetCopula(new TCopula(df, corr));
        else
            demandModel.SetCorrelation(corr); // default Gaussian

        // --- Damage + Loss (simplified but consistent)
        var damageModel = new DamageModel();
        var lossModel = new LossModel();

        for (int s = 1; s <= Stories; s++)
        {
            var comp = new Component($"B1035-{s}", pidEdps[s - 1], 4, s);
            comp.AddDamageState(new DamageState(0, "None"));
            comp.AddDamageState(new DamageState(1, "Minor"));
            comp.AddDamageState(new DamageState(2, "Moderate"));
            comp.AddDamageState(new DamageState(3, "Collapse"));
            asset.AddComponent(comp);

            var spec = new ComponentFragilitySpec(comp);
            spec.AddFragilityFunction(new FragilityFunction(0.006, 0.35));
            spec.AddFragilityFunction(new FragilityFunction(0.02, 0.35));
            spec.AddFragilityFunction(new FragilityFunction(0.05, 0.35));
            damageModel.Add(spec);

            lossModel.AddConsequence(comp.Id,
                new ConsequenceFunction(3, DecisionVariable.Cost, 500_000, 0.4));
        }

        var pipeline = new SimulationPipeline(asset)
            .WithDemand(demandModel)
            .WithDamage(damageModel)
            .WithLoss(lossModel)
            .WithSampler(LatinHypercubeSampler.Standard)
            .WithDecisionVariables(DecisionVariable.Cost);

        return (pipeline, corr);
    }
    [Fact]
    public void TCopula_ShouldProduceExtremeUniforms()
    {
        int N = 100_000;

        var u = LatinHypercubeSampler.Standard.GenerateUniform(N, 2, 42);

        new TCopula(3.0, new double[,] { { 1, 0.5 }, { 0.5, 1 } }, 42)
            .ApplyCorrelation(u);

        double max = 0;
        for (int i = 0; i < N; i++)
            max = Math.Max(max, u[i, 0]);

        Assert.True(max > 0.999, $"Max too small: {max}");
    }
    [Fact]
    public void TCopula_ShouldIncreaseUpperTailPercentiles()
    {
        const int N = 50_000;
        const int seed = 2024;

        var (gPipeline, corr) = BuildAdvancedPipeline(false);
        var g = gPipeline.Run(N, seed);

        var (tPipeline, _) = BuildAdvancedPipeline(true, corr, df: 3);
        var t = tPipeline.Run(N, seed);

        double g95 = g.CostAtPercentile(0.95);
        double t95 = t.CostAtPercentile(0.95);

        double g99 = g.CostAtPercentile(0.99);
        double t99 = t.CostAtPercentile(0.99);

        Assert.True(t95 >= g95,
            $"t-copula should increase 95th percentile: t={t95}, g={g95}");

        Assert.True(t99 >= g99,
            $"t-copula should increase 99th percentile: t={t99}, g={g99}");
    }

    [Fact]
    public void TCopula_ShouldIncreaseExtremeLossMean()
    {
        const int N = 50_000;
        const int seed = 2024;

        var (gPipeline, corr) = BuildAdvancedPipeline(false);
        var g = gPipeline.Run(N, seed);

        var (tPipeline, _) = BuildAdvancedPipeline(true, corr, df: 3);
        var t = tPipeline.Run(N, seed);

        double gExtreme = g.TopKMean(0.01); // أعلى 1%
        double tExtreme = t.TopKMean(0.01);

        Assert.True(tExtreme >= gExtreme,
            $"t-copula should increase extreme mean: t={tExtreme}, g={gExtreme}");
    }

    [Fact]
    public void TCopula_ShouldIncreaseCVaR()
    {
        const int N = 50_000;
        const int seed = 2024;

        var (gPipeline, corr) = BuildAdvancedPipeline(false);
        var g = gPipeline.Run(N, seed);

        var (tPipeline, _) = BuildAdvancedPipeline(true, corr, df: 3);
        var t = tPipeline.Run(N, seed);

        double gCvar = g.CostCVaR(0.95);
        double tCvar = t.CostCVaR(0.95);

        Assert.True(tCvar >= gCvar,
            $"t-copula should increase CVaR: t={tCvar}, g={gCvar}");
    }
}
