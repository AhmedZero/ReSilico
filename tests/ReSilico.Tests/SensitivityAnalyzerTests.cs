// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SensitivityAnalyzerTests: sign consistency, global Sobol, correlation direction, validation

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Sensitivity;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Copulas;
using ReSilico.Domain;
using ReSilico.Domain.Enums;
using System;
using System.Linq;

namespace ReSilico.Tests;

public class SensitivityAnalyzerTests
{
    // ─── Shared constants ─────────────────────────────────────────────────────

    private const int N    = 10_000;
    private const int Seed = 42;

    // ─── Pipeline helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Simple 1-EDP, 1-component pipeline designed to produce significant
    /// and stable sensitivity signals:
    ///   • EDP median = 0.01 rad = LS1 fragility median → P(DS≥1) ≈ 50%
    ///   • LS2 fragility median = 0.04 (well above EDP median) → P(DS≥2) ≈ 13%
    ///
    /// This placement maximises ∂P(damage)/∂(fragility median) and ensures
    /// non-trivial loss for every plausible perturbation.
    /// </summary>
    private static SimulationPipeline BuildSingleComponentPipeline()
    {
        var edp  = new EDP("PID", 1, 1, "rad");
        var comp = new Component("C1", edp, 1.0, 1);
        comp.AddDamageState(new DamageState(0, "None"));
        comp.AddDamageState(new DamageState(1, "Minor"));
        comp.AddDamageState(new DamageState(2, "Severe"));

        var asset = new Asset("T1", "Test Asset", HazardType.Earthquake);
        asset.AddComponent(comp);

        var demand = new DemandModel();
        // Theta1 = ln(0.01) ≈ −4.605 ; Theta2 = 0.4 (dispersion)
        demand.AddEdp(new EdpDistributionSpec(
            edp, EdpDistributionKind.Lognormal, Math.Log(0.01), 0.4));

        var damage = new DamageModel();
        var fSpec  = new ComponentFragilitySpec(comp);
        fSpec.AddFragilityFunction(new FragilityFunction(0.01, 0.35, "LS1"));
        fSpec.AddFragilityFunction(new FragilityFunction(0.04, 0.35, "LS2"));
        damage.Add(fSpec);

        var loss = new LossModel();
        loss.AddConsequence("C1", new ConsequenceFunction(1, DecisionVariable.Cost,  50_000, 0.30));
        loss.AddConsequence("C1", new ConsequenceFunction(2, DecisionVariable.Cost, 200_000, 0.30));

        return new SimulationPipeline(asset)
            .WithDemand(demand)
            .WithDamage(damage)
            .WithLoss(loss);
    }

    /// <summary>
    /// Two-EDP, two-component pipeline with a Gaussian correlation matrix.
    /// Designed so increasing ρ produces a visible increase in CVaR.
    /// Both components use the same EDP statistics and fragility thresholds
    /// (symmetric, so the signal is purely correlation-driven).
    /// </summary>
    private static SimulationPipeline BuildCorrelatedPipeline(double rho = 0.50)
    {
        var edp1 = new EDP("PID", 1, 1, "rad");
        var edp2 = new EDP("PID", 2, 1, "rad");

        var comp1 = new Component("C1", edp1, 1.0, 1);
        var comp2 = new Component("C2", edp2, 1.0, 2);

        foreach (var c in new[] { comp1, comp2 })
        {
            c.AddDamageState(new DamageState(0, "None"));
            c.AddDamageState(new DamageState(1, "Minor"));
            c.AddDamageState(new DamageState(2, "Severe"));
        }

        var asset = new Asset("T2", "Correlated Asset", HazardType.Earthquake);
        asset.AddComponent(comp1);
        asset.AddComponent(comp2);

        var demand = new DemandModel();
        demand.AddEdp(new EdpDistributionSpec(edp1, EdpDistributionKind.Lognormal, Math.Log(0.01), 0.4));
        demand.AddEdp(new EdpDistributionSpec(edp2, EdpDistributionKind.Lognormal, Math.Log(0.01), 0.4));
        demand.SetCorrelation(new double[,] { { 1, rho }, { rho, 1 } });

        var damage = new DamageModel();
        foreach (var (c, e) in new[] { (comp1, edp1), (comp2, edp2) })
        {
            var fs = new ComponentFragilitySpec(c);
            fs.AddFragilityFunction(new FragilityFunction(0.008, 0.35, "LS1"));
            fs.AddFragilityFunction(new FragilityFunction(0.025, 0.35, "LS2"));
            damage.Add(fs);
        }

        var loss = new LossModel();
        loss.AddConsequence("C1", new ConsequenceFunction(1, DecisionVariable.Cost,  50_000, 0.25));
        loss.AddConsequence("C1", new ConsequenceFunction(2, DecisionVariable.Cost, 200_000, 0.25));
        loss.AddConsequence("C2", new ConsequenceFunction(1, DecisionVariable.Cost,  50_000, 0.25));
        loss.AddConsequence("C2", new ConsequenceFunction(2, DecisionVariable.Cost, 200_000, 0.25));

        return new SimulationPipeline(asset)
            .WithDemand(demand)
            .WithDamage(damage)
            .WithLoss(loss);
    }

    // ─── Options helpers ──────────────────────────────────────────────────────

    private static SensitivityOptions LocalMeanOptions() => new()
    {
        SampleSize       = N,
        Seed             = Seed,
        Method           = SensitivityMethod.Local,
        TargetStatistic  = SensitivityStatistic.Mean,
        PerturbationFactor = 0.05,
    };

    private static SensitivityOptions LocalCVaROptions() => new()
    {
        SampleSize       = N,
        Seed             = Seed,
        Method           = SensitivityMethod.Local,
        TargetStatistic  = SensitivityStatistic.CVaR,
        TargetQuantile   = 0.90,
        PerturbationFactor = 0.05,
    };

    // ─── 1. Sign consistency (Local) ─────────────────────────────────────────

    /// <summary>
    /// Increasing the fragility LS1 median (harder to damage) → lower mean loss.
    /// Sensitivity of mean cost w.r.t. fragility LS1 median must be negative.
    /// </summary>
    [Fact]
    public void LocalSensitivity_FragilityLS1Median_IsNegative()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        var entry = result.Entries
            .Single(e => e.Parameter == "Fragility[C1][LS1].Median");

        Assert.True(entry.SensitivityValue < 0,
            $"Expected negative S for fragility median (harder to damage = less loss), " +
            $"got S = {entry.SensitivityValue:G4}");
    }

    /// <summary>
    /// Increasing EDP Theta1 (log-mean) → higher demand median → more damage → higher mean loss.
    /// For a lognormal EDP, Theta1 = ln(median) is negative (e.g., ≈ −4.6).
    /// The FD formula handles negative θ correctly:
    ///   (f+ − f−) is negative (lower demand ↔ higher absolute θ)
    ///   2ε·θ is also negative → S = negative/negative = positive.
    /// </summary>
    [Fact]
    public void LocalSensitivity_EdpTheta1_IsPositive()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        var entry = result.Entries
            .Single(e => e.Parameter == "EDP[PID-1-1].Theta1");

        Assert.True(entry.SensitivityValue > 0,
            $"Expected positive S for EDP log-mean (higher demand = more loss), " +
            $"got S = {entry.SensitivityValue:G4}");
    }

    /// <summary>
    /// Increasing the fragility LS1 dispersion (Beta) makes the fragility curve flatter,
    /// spreading damage probability toward both extremes.  At the chosen EDP median
    /// (equal to the LS1 fragility median), a flatter fragility reduces P(damage) in
    /// the right tail → mean loss should decrease → negative sensitivity.
    ///
    /// This test also verifies sign consistency for a second parameter type.
    /// </summary>
    [Fact]
    public void LocalSensitivity_FragilityLS2Median_IsNegative()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        var entry = result.Entries
            .Single(e => e.Parameter == "Fragility[C1][LS2].Median");

        // Higher LS2 median → harder to reach DS2 → lower mean of high-consequence events.
        Assert.True(entry.SensitivityValue < 0,
            $"Expected negative S for LS2 fragility median, got S = {entry.SensitivityValue:G4}");
    }

    // ─── 2. Result integrity ──────────────────────────────────────────────────

    [Fact]
    public void LocalSensitivity_AllEntries_AreFinite()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        Assert.NotEmpty(result.Entries);

        foreach (var e in result.Entries)
        {
            Assert.False(double.IsNaN(e.SensitivityValue),
                $"NaN sensitivity for parameter '{e.Parameter}'");
            Assert.False(double.IsInfinity(e.SensitivityValue),
                $"Infinite sensitivity for parameter '{e.Parameter}'");
            Assert.True(e.BaselineValue > 0,
                "Baseline loss should be positive (damage occurs for chosen EDP/fragility setup)");
        }
    }

    [Fact]
    public void LocalSensitivity_BaselineValue_IsPositive()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        Assert.True(result.BaselineValue > 0,
            $"Expected positive baseline mean cost, got {result.BaselineValue}");
    }

    [Fact]
    public void LocalSensitivity_ParameterGroups_CoverEDPFragilityLoss()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        var groups = result.Entries.Select(e => e.ParameterGroup).Distinct().ToHashSet();

        Assert.Contains("EDP",       groups);
        Assert.Contains("Fragility", groups);
        Assert.Contains("Loss",      groups);
    }

    // ─── 3. Ranked ordering ───────────────────────────────────────────────────

    [Fact]
    public void Ranked_ReturnsMostInfluentialFirst()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        var ranked = result.Ranked;
        Assert.NotEmpty(ranked);

        for (int i = 0; i < ranked.Count - 1; i++)
            Assert.True(
                Math.Abs(ranked[i].SensitivityValue) >=
                Math.Abs(ranked[i + 1].SensitivityValue),
                $"Ranked list is not ordered at index {i}: " +
                $"|S[{i}]|={Math.Abs(ranked[i].SensitivityValue):G4} < " +
                $"|S[{i+1}]|={Math.Abs(ranked[i+1].SensitivityValue):G4}");
    }

    // ─── 4. Global sensitivity (Morris / Sobol approximation) ────────────────

    [Fact]
    public void GlobalSensitivity_SobolApproximation_SumsToOne()
    {
        var pipeline = BuildSingleComponentPipeline();
        var options  = new SensitivityOptions
        {
            SampleSize      = N,
            Seed            = Seed,
            Method          = SensitivityMethod.Global,
            TargetStatistic = SensitivityStatistic.Mean,
            GlobalLevels    = 5,
        };

        var result = new SensitivityAnalyzer().Analyze(pipeline, options);

        Assert.NotEmpty(result.Entries);

        double sum = result.Entries.Sum(e => e.RelativeSensitivity);

        // Approximate Sobol indices sum to 1 (first-order, no interactions).
        Assert.InRange(sum, 0.90, 1.10);
    }

    [Fact]
    public void GlobalSensitivity_MuStar_IsNonNegative()
    {
        var pipeline = BuildSingleComponentPipeline();
        var options  = new SensitivityOptions
        {
            SampleSize      = N,
            Seed            = Seed,
            Method          = SensitivityMethod.Global,
            TargetStatistic = SensitivityStatistic.Mean,
            GlobalLevels    = 5,
        };

        var result = new SensitivityAnalyzer().Analyze(pipeline, options);

        foreach (var e in result.Entries)
            Assert.True(e.SensitivityValue >= 0,
                $"Morris μ* must be ≥ 0 (it is a mean of absolute values); " +
                $"parameter '{e.Parameter}' returned {e.SensitivityValue:G4}");
    }

    // ─── 5. Correlation sensitivity direction ─────────────────────────────────

    /// <summary>
    /// With two positively-correlated EDPs driving identical components, increasing ρ
    /// makes joint extremes more likely, raising the aggregate loss in the tail.
    /// CVaR sensitivity w.r.t. ρ must be positive.
    /// </summary>
    [Fact]
    public void CorrelationSensitivity_HigherRho_IncreasesLossCVaR()
    {
        var pipeline = BuildCorrelatedPipeline(rho: 0.50);
        var options  = new SensitivityOptions
        {
            SampleSize         = N,
            Seed               = Seed,
            Method             = SensitivityMethod.Correlation,
            TargetStatistic    = SensitivityStatistic.CVaR,
            TargetQuantile     = 0.90,
            PerturbationFactor = 0.10,  // wider delta to overcome sampling noise
        };

        var result = new SensitivityAnalyzer().Analyze(pipeline, options);

        Assert.Single(result.Entries); // only one off-diagonal pair in a 2×2 matrix

        var entry = result.Entries[0];
        Assert.True(entry.SensitivityValue >= 0,
            $"Correlation sensitivity for CVaR should be ≥ 0 " +
            $"(higher ρ → more joint tail damage). Got S = {entry.SensitivityValue:G4}");
    }

    [Fact]
    public void CorrelationSensitivity_HigherRho_IncreasesLossCVaR_TCopula()
    {
        // Same test but with a t-Copula instead of a Gaussian copula.
        var edp1 = new EDP("PID", 1, 1, "rad");
        var edp2 = new EDP("PID", 2, 1, "rad");
        var comp1 = new Component("C1", edp1, 1.0, 1);
        var comp2 = new Component("C2", edp2, 1.0, 2);

        foreach (var c in new[] { comp1, comp2 })
        {
            c.AddDamageState(new DamageState(0, "None"));
            c.AddDamageState(new DamageState(1, "Minor"));
            c.AddDamageState(new DamageState(2, "Severe"));
        }

        var asset = new Asset("T3", "TCopula Asset", HazardType.Earthquake);
        asset.AddComponent(comp1);
        asset.AddComponent(comp2);

        var demand = new DemandModel();
        demand.AddEdp(new EdpDistributionSpec(edp1, EdpDistributionKind.Lognormal, Math.Log(0.01), 0.4));
        demand.AddEdp(new EdpDistributionSpec(edp2, EdpDistributionKind.Lognormal, Math.Log(0.01), 0.4));
        double[,] rho0 = { { 1, 0.50 }, { 0.50, 1 } };
        demand.SetCopula(new TCopula(degreesOfFreedom: 5.0, rho0, seed: Seed));

        var damage = new DamageModel();
        foreach (var c in new[] { comp1, comp2 })
        {
            var fs = new ComponentFragilitySpec(c);
            fs.AddFragilityFunction(new FragilityFunction(0.008, 0.35, "LS1"));
            fs.AddFragilityFunction(new FragilityFunction(0.025, 0.35, "LS2"));
            damage.Add(fs);
        }

        var loss = new LossModel();
        foreach (string cid in new[] { "C1", "C2" })
        {
            loss.AddConsequence(cid, new ConsequenceFunction(1, DecisionVariable.Cost, 50_000, 0.25));
            loss.AddConsequence(cid, new ConsequenceFunction(2, DecisionVariable.Cost, 200_000, 0.25));
        }

        var pipeline = new SimulationPipeline(asset)
            .WithDemand(demand)
            .WithDamage(damage)
            .WithLoss(loss);

        var options = new SensitivityOptions
        {
            SampleSize         = N,
            Seed               = Seed,
            Method             = SensitivityMethod.Correlation,
            TargetStatistic    = SensitivityStatistic.CVaR,
            TargetQuantile     = 0.90,
            PerturbationFactor = 0.10,
        };

        var result = new SensitivityAnalyzer().Analyze(pipeline, options);

        Assert.Single(result.Entries);
        var entry = result.Entries[0];
        Assert.Equal("Correlation", entry.ParameterGroup);
        Assert.True(entry.SensitivityValue >= 0,
            $"t-Copula: correlation sensitivity should be ≥ 0. Got {entry.SensitivityValue:G4}");
    }

    // ─── 6. Small perturbation → smooth response ──────────────────────────────

    /// <summary>
    /// Verifies that perturbing EDP Theta1 produces a monotone, smooth response:
    ///   • fMinus (lower |theta1| → higher EDP median) must produce MORE loss than fPlus.
    ///   • Both perturbed outputs must be strictly positive (no degenerate collapse).
    ///   • The relative change is detectable (> 0.1 %) confirming numerical stability.
    ///
    /// Note: a 5 % multiplicative perturbation in ln-space corresponds to ~26 %
    /// change in the actual EDP median, so the relative response can be large —
    /// the test does NOT bound the upper end.
    /// </summary>
    [Fact]
    public void LocalSensitivity_EDP_PerturbationIsMonotone()
    {
        var pipeline = BuildSingleComponentPipeline();
        var options  = LocalMeanOptions();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, options);

        var e = result.Entries.Single(x => x.Parameter == "EDP[PID-1-1].Theta1");

        // theta1 is negative; theta1+ = theta1 × 1.05 is more negative → lower EDP median
        // → less damage → less loss.  So fPlus < fMinus.
        Assert.True(e.PerturbedPlusValue < e.PerturbedMinusValue,
            $"Expected f(theta1+) < f(theta1-) for lognormal EDP with negative theta1. " +
            $"f+ = {e.PerturbedPlusValue:N0}, f- = {e.PerturbedMinusValue:N0}");

        // Both outputs are positive (no degenerate collapse to zero loss)
        Assert.True(e.PerturbedPlusValue  > 0, "f(theta1+) should be positive");
        Assert.True(e.PerturbedMinusValue > 0, "f(theta1-) should be positive");

        // Change is detectable (> 0.1 % of baseline)
        double relChange = Math.Abs(e.PerturbedMinusValue - e.PerturbedPlusValue) / e.BaselineValue;
        Assert.True(relChange > 0.001,
            $"Expected a detectable sensitivity; relative change = {relChange:P2}");
    }

    // ─── 7. Validation ───────────────────────────────────────────────────────

    [Fact]
    public void Analyze_PipelineMissingDemandModel_Throws()
    {
        var edp  = new EDP("PID", 1);
        var comp = new Component("C1", edp, 1.0);
        comp.AddDamageState(new DamageState(0, "None"));
        comp.AddDamageState(new DamageState(1, "DS1"));

        var asset  = new Asset("X", "X", HazardType.Earthquake);
        asset.AddComponent(comp);

        var damage = new DamageModel();
        var fSpec  = new ComponentFragilitySpec(comp);
        fSpec.AddFragilityFunction(new FragilityFunction(0.01, 0.35));
        damage.Add(fSpec);

        var loss = new LossModel();
        loss.AddConsequence("C1", new ConsequenceFunction(1, DecisionVariable.Cost, 10_000));

        // Pipeline has damage + loss but NO demand model
        var pipeline = new SimulationPipeline(asset)
            .WithDamage(damage)
            .WithLoss(loss);

        Assert.Throws<InvalidOperationException>(() =>
            new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions()));
    }

    [Fact]
    public void Analyze_CorrelationMethod_WithoutCorrelationOnDemand_Throws()
    {
        // Single-component pipeline without any correlation matrix → Correlation method fails.
        var pipeline = BuildSingleComponentPipeline();
        var options  = new SensitivityOptions
        {
            SampleSize = N,
            Seed       = Seed,
            Method     = SensitivityMethod.Correlation,
        };

        Assert.Throws<InvalidOperationException>(() =>
            new SensitivityAnalyzer().Analyze(pipeline, options));
    }

    [Fact]
    public void Analyze_InvalidPerturbationFactor_Throws()
    {
        var pipeline = BuildSingleComponentPipeline();
        var options  = LocalMeanOptions();
        options.PerturbationFactor = 0.0; // invalid

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SensitivityAnalyzer().Analyze(pipeline, options));
    }

    // ─── 8. Relative sensitivity (elasticity) ────────────────────────────────

    [Fact]
    public void LocalSensitivity_RelativeSensitivity_IsFinite()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        foreach (var e in result.Entries)
            Assert.False(double.IsNaN(e.RelativeSensitivity) && e.BaselineValue > 0,
                $"RelativeSensitivity is NaN for '{e.Parameter}' despite positive baseline");
    }

    [Fact]
    public void LocalSensitivity_LossMedian_IsPositive()
    {
        var pipeline = BuildSingleComponentPipeline();
        var result   = new SensitivityAnalyzer().Analyze(pipeline, LocalMeanOptions());

        // Higher consequence median → more loss when that DS is reached → positive S.
        var entry = result.Entries
            .FirstOrDefault(e => e.Parameter.Contains("Loss") && e.Parameter.Contains("Median"));

        Assert.NotNull(entry);
        Assert.True(entry.SensitivityValue > 0,
            $"Expected positive S for consequence median (more loss = more cost), " +
            $"got S = {entry.SensitivityValue:G4}");
    }

    // ─── 9. TargetStatistic variants ─────────────────────────────────────────

    [Theory]
    [InlineData(SensitivityStatistic.Mean)]
    [InlineData(SensitivityStatistic.StdDev)]
    [InlineData(SensitivityStatistic.Percentile)]
    [InlineData(SensitivityStatistic.CVaR)]
    public void LocalSensitivity_AllTargetStatistics_Succeed(SensitivityStatistic stat)
    {
        var pipeline = BuildSingleComponentPipeline();
        var options  = LocalMeanOptions();
        options.TargetStatistic = stat;
        options.TargetQuantile  = 0.90;
        options.SampleSize      = 5_000; // smaller for speed

        var result = new SensitivityAnalyzer().Analyze(pipeline, options);

        Assert.NotEmpty(result.Entries);
        Assert.True(result.BaselineValue >= 0);
        Assert.Contains(stat.ToString(), result.TargetStatistic,
            StringComparison.OrdinalIgnoreCase);
    }

    // ─── 10. Determinism ─────────────────────────────────────────────────────

    [Fact]
    public void LocalSensitivity_SameSeed_ProducesSameResults()
    {
        var options = LocalMeanOptions();

        double s1 = new SensitivityAnalyzer()
            .Analyze(BuildSingleComponentPipeline(), options)
            .Entries.Single(e => e.Parameter == "EDP[PID-1-1].Theta1")
            .SensitivityValue;

        double s2 = new SensitivityAnalyzer()
            .Analyze(BuildSingleComponentPipeline(), options)
            .Entries.Single(e => e.Parameter == "EDP[PID-1-1].Theta1")
            .SensitivityValue;

        Assert.Equal(s1, s2, precision: 10);
    }
}
