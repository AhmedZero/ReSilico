// ReSilico – Probabilistic Damage and Loss Assessment Engine
// AdaptiveSimulationTests: convergence, efficiency, stability, tail accuracy

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using System;
using System.Linq;

namespace ReSilico.Tests;

/// <summary>
/// Tests for <see cref="SimulationPipeline.RunAdaptive"/>.
///
/// All tests use the same minimal single-component pipeline:
///   EDP:    Lognormal(μ_ln = ln 0.007, β = 0.40)
///   Damage: one limit state at median 0.005, β=0.40 → ~P(DS≥1|EDP) ≈ 0.7
///   Loss:   DS1 → Cost Lognormal(median $20 000, β=0.35)
/// </summary>
public class AdaptiveSimulationTests
{
    // ─── Shared fixture ───────────────────────────────────────────────────────

    private static SimulationPipeline BuildPipeline()
    {
        var edp  = new EDP("PID", 1, 1);
        var comp = new Component("C1", edp, quantity: 1.0);

        var asset = new Asset("Building", "Test");
        asset.AddComponent(comp);

        var demandModel = new DemandModel();
        demandModel.AddEdp(new EdpDistributionSpec(
            edp, EdpDistributionKind.Lognormal, Math.Log(0.007), 0.40));

        var fragSpec = new ComponentFragilitySpec(comp);
        fragSpec.AddFragilityFunction(new FragilityFunction(0.005, 0.40));
        var damageModel = new DamageModel();
        damageModel.Add(fragSpec);

        var lossModel = new LossModel();
        lossModel.AddConsequence("C1",
            new ConsequenceFunction(1, DecisionVariable.Cost, 20_000, beta: 0.35));

        return new SimulationPipeline(asset)
            .WithDemand(demandModel)
            .WithDamage(damageModel)
            .WithLoss(lossModel)
            .WithSampler(LatinHypercubeSampler.Standard)
            .WithDecisionVariables(DecisionVariable.Cost);
    }

    // ─── 1. Type hierarchy ────────────────────────────────────────────────────

    [Fact]
    public void AdaptiveResult_IsAssignableFrom_SimulationResult()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 500, BatchSize = 200, MaxSamples = 1_000,
            ToleranceMean = 1.0, ToleranceTail = 1.0,   // always converge in 2 iterations
            Seed = 1
        });

        Assert.IsAssignableFrom<SimulationResult>(r);
        Assert.IsType<AdaptiveSimulationResult>(r);
    }

    // ─── 2. Convergence ───────────────────────────────────────────────────────

    [Fact]
    public void Adaptive_LooseTolerance_ConvergesBeforeMaxSamples()
    {
        var opts = new AdaptiveOptions
        {
            InitialSamples = 3_000,
            BatchSize      = 2_000,
            MaxSamples     = 100_000,
            ToleranceMean  = 0.05,    // 5 % — achievable for this system
            ToleranceTail  = 0.10,    // 10 %
            TargetQuantile = 0.95,
            Seed           = 42
        };

        var r = BuildPipeline().RunAdaptive(opts);

        Assert.True(r.Converged,
            $"Expected convergence but stopped at {r.NumberOfSimulations} samples " +
            $"(mean err={r.FinalMeanRelativeError:P2})");
        Assert.True(r.NumberOfSimulations < opts.MaxSamples,
            $"Used all MaxSamples ({opts.MaxSamples}) without converging — " +
            $"tolerance may be too tight for this sample budget.");
    }

    [Fact]
    public void Adaptive_TightTolerance_HitsMaxSamplesWithoutConverging()
    {
        // Near-impossible tolerance ensures the loop always hits the cap.
        var opts = new AdaptiveOptions
        {
            InitialSamples = 500,
            BatchSize      = 500,
            MaxSamples     = 3_000,
            ToleranceMean  = 1e-8,
            ToleranceTail  = 1e-8,
            Seed           = 10
        };

        var r = BuildPipeline().RunAdaptive(opts);

        Assert.False(r.Converged, "Expected non-convergence with near-zero tolerance.");
        Assert.InRange(r.NumberOfSimulations, 1, opts.MaxSamples);
    }

    // ─── 3. Diagnostics integrity ─────────────────────────────────────────────

    [Fact]
    public void Diagnostics_CountMatchesIterations()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 300,
            BatchSize      = 200,
            MaxSamples     = 1_500,
            ToleranceMean  = 1.0,    // always converges after 2 iterations
            ToleranceTail  = 1.0,
            Seed           = 7
        });

        Assert.Equal(r.Iterations, r.Diagnostics.Count);
        Assert.True(r.Iterations >= 2, "At least 2 iterations are always executed.");
    }

    [Fact]
    public void Diagnostics_TotalSamples_IsNonDecreasing()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 300,
            BatchSize      = 200,
            MaxSamples     = 2_000,
            ToleranceMean  = 1e-8,    // force all batches to run
            ToleranceTail  = 1e-8,
            Seed           = 5
        });

        var totals = r.Diagnostics.Select(d => d.TotalSamples).ToList();
        for (int i = 1; i < totals.Count; i++)
            Assert.True(totals[i] > totals[i - 1],
                $"Total samples not increasing: {totals[i - 1]} → {totals[i]}");
    }

    [Fact]
    public void Diagnostics_FirstIteration_TailRelativeChange_IsInfinity()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 200, BatchSize = 200, MaxSamples = 1_000,
            ToleranceMean = 1e-8, ToleranceTail = 1e-8,
            Seed = 3
        });

        Assert.True(double.IsPositiveInfinity(r.Diagnostics[0].TailRelativeChange),
            "First iteration has no previous CVaR — tail change should be +∞.");
    }

    // ─── 4. Statistical sanity ────────────────────────────────────────────────

    [Fact]
    public void AdaptiveResult_CVaR_IsGreaterThanOrEqualTo_Percentile_And_Mean()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 2_000, BatchSize = 1_000, MaxSamples = 5_000,
            ToleranceMean = 1e-8, ToleranceTail = 1e-8,
            TargetQuantile = 0.95, Seed = 42
        });

        double mean = r.MeanCost;
        double p95  = r.CostAtPercentile(0.95);
        double cvar = r.CostCVaR(0.95);

        Assert.True(cvar >= p95,
            $"CVaR({cvar:N0}) must be ≥ 95th percentile ({p95:N0}).");
        Assert.True(p95 >= mean,
            $"95th percentile ({p95:N0}) must be ≥ mean ({mean:N0}).");
    }

    [Fact]
    public void AdaptiveResult_AllDiagnostics_CVaR_GeaterThanOrEqualTo_Mean()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 500, BatchSize = 300, MaxSamples = 3_000,
            ToleranceMean = 1e-8, ToleranceTail = 1e-8,
            TargetQuantile = 0.95, Seed = 11
        });

        Assert.All(r.Diagnostics, d =>
            Assert.True(d.CVaR >= d.MeanCost,
                $"Iteration {d.Iteration}: CVaR {d.CVaR:N0} < mean {d.MeanCost:N0}"));
    }

    [Fact]
    public void AdaptiveResult_MeanCost_IsPositive()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 1_000, BatchSize = 500, MaxSamples = 3_000,
            ToleranceMean = 1e-8, ToleranceTail = 1e-8,
            Seed = 77
        });

        Assert.True(r.MeanCost > 0,
            $"Expected positive mean cost but got {r.MeanCost:N2}");
    }

    // ─── 5. Reproducibility ───────────────────────────────────────────────────

    [Fact]
    public void Adaptive_SameSeed_ProducesIdenticalResults()
    {
        var opts = new AdaptiveOptions
        {
            InitialSamples = 1_000, BatchSize = 500, MaxSamples = 4_000,
            ToleranceMean = 0.08, ToleranceTail = 0.12,
            Seed = 99
        };

        var r1 = BuildPipeline().RunAdaptive(opts);
        var r2 = BuildPipeline().RunAdaptive(opts);

        Assert.Equal(r1.NumberOfSimulations, r2.NumberOfSimulations);
        Assert.Equal(r1.Converged,           r2.Converged);
        Assert.Equal(r1.Iterations,          r2.Iterations);
        Assert.Equal(r1.MeanCost,            r2.MeanCost,  precision: 10);
        Assert.Equal(r1.FinalCVaR,           r2.FinalCVaR, precision: 10);
    }

    // ─── 6. Efficiency ────────────────────────────────────────────────────────

    [Fact]
    public void Adaptive_ConvergedRun_UsesFarFewerSamplesThanMaxSamples()
    {
        var opts = new AdaptiveOptions
        {
            InitialSamples = 3_000,
            BatchSize      = 2_000,
            MaxSamples     = 50_000,
            ToleranceMean  = 0.05,
            ToleranceTail  = 0.08,
            Seed           = 42
        };

        var r = BuildPipeline().RunAdaptive(opts);

        // Expect to converge well before the MaxSamples ceiling.
        Assert.True(r.Converged, "Run did not converge within MaxSamples.");
        Assert.True(r.NumberOfSimulations <= opts.MaxSamples / 2,
            $"Converged run used {r.NumberOfSimulations} samples — " +
            $"expected much less than half of MaxSamples ({opts.MaxSamples}).");
    }

    // ─── 7. Tail accuracy ─────────────────────────────────────────────────────

    [Fact]
    public void Adaptive_CVaR_IsCloseToLargeFixedRunCVaR()
    {
        // Reference: large fixed-MC run as ground truth.
        var pipeline = BuildPipeline();
        SimulationResult reference = pipeline.Run(numberOfSimulations: 50_000, seed: 50);
        double refCVaR = reference.CostCVaR(0.95);
        Assert.True(refCVaR > 0, "Reference CVaR must be positive.");

        // Adaptive run with tight tail tolerance.
        AdaptiveSimulationResult adaptive = pipeline.RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 3_000,
            BatchSize      = 2_000,
            MaxSamples     = 80_000,
            ToleranceMean  = 0.03,
            ToleranceTail  = 0.05,
            TargetQuantile = 0.95,
            Seed           = 55
        });

        double ratio = adaptive.FinalCVaR / refCVaR;

        // CVaR from a converged adaptive run should be within ±25 % of reference.
        // This wide band accounts for inherent MC variability between different seed streams.
        Assert.InRange(ratio, 0.75, 1.25);
    }

    // ─── 8. SimulationResult API still works on adaptive result ──────────────

    [Fact]
    public void AdaptiveResult_HistogramAndExceedance_WorkCorrectly()
    {
        var r = BuildPipeline().RunAdaptive(new AdaptiveOptions
        {
            InitialSamples = 500, BatchSize = 300, MaxSamples = 2_000,
            ToleranceMean = 1e-8, ToleranceTail = 1e-8, Seed = 22
        });

        var (edges, counts) = r.CostHistogram(bins: 10);
        Assert.Equal(11, edges.Length);   // bins + 1 edges
        Assert.Equal(10, counts.Length);
        Assert.Equal(r.NumberOfSimulations, counts.Sum());

        double exceedance = r.CostExceedanceProbability(0.0);
        Assert.InRange(exceedance, 0.0, 1.0);
    }

    // ─── 9. Options validation ────────────────────────────────────────────────

    [Fact]
    public void RunAdaptive_InvalidOptions_Throws()
    {
        var pipeline = BuildPipeline();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pipeline.RunAdaptive(new AdaptiveOptions { InitialSamples = 5 }));   // < 10

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pipeline.RunAdaptive(new AdaptiveOptions
            { InitialSamples = 5_000, MaxSamples = 100 }));   // max < initial
    }
}
