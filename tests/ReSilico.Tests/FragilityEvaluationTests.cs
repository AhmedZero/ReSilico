// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Unit tests: Fragility evaluation, damage model, pipeline correctness

using Xunit;
using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;

namespace ReSilico.Tests;

public class FragilityEvaluationTests
{
    // ─── Fragility scalar evaluation ──────────────────────────────────────────

    [Theory]
    [InlineData(0.005, 0.4)]
    [InlineData(0.010, 0.5)]
    public void FragilityFunction_AtMedian_IsHalfProbability(double median, double beta)
    {
        var frag = new FragilityFunction(median, beta);
        Assert.InRange(frag.Evaluate(median), 0.499, 0.501);
    }

    [Fact]
    public void FragilityFunction_Zero_EDP_Returns_Zero()
    {
        Assert.Equal(0.0, new FragilityFunction(0.005, 0.4).Evaluate(0.0));
        Assert.Equal(0.0, new FragilityFunction(0.005, 0.4).Evaluate(-1.0));
    }

    [Fact]
    public void FragilityFunction_IsMonotone()
    {
        var frag = new FragilityFunction(0.005, 0.4);
        double prev = 0.0;
        for (double edp = 1e-6; edp <= 0.05; edp += 0.0005)
        {
            double p = frag.Evaluate(edp);
            Assert.True(p >= prev - 1e-12, $"Non-monotone at {edp}");
            prev = p;
        }
    }

    [Fact]
    public void FragilityFunction_ZScore_MatchesFormula()
    {
        double theta = 0.005, beta = 0.40, edp = 0.010;
        var frag = new FragilityFunction(theta, beta);
        double expected = (Math.Log(edp) - Math.Log(theta)) / beta;
        Assert.Equal(expected, frag.ZScore(edp), precision: 10);
    }

    [Fact]
    public void FragilityFunction_Evaluate_MatchesLognormalCDF()
    {
        double theta = 0.005, beta = 0.40, edp = 0.0075;
        double zScore = (Math.Log(edp) - Math.Log(theta)) / beta;
        double expected = NormalDistribution.StandardCDF(zScore);
        Assert.Equal(expected, new FragilityFunction(theta, beta).Evaluate(edp), precision: 9);
    }

    // ─── Batch evaluation ─────────────────────────────────────────────────────

    [Fact]
    public void FragilityFunction_BatchEvaluate_MatchesScalar()
    {
        var frag = new FragilityFunction(0.005, 0.4);
        double[] edps = [0.001, 0.003, 0.005, 0.007, 0.010, 0.015, 0.020, 0.030];
        double[] expected = [.. edps.Select(e => frag.Evaluate(e))];
        double[] actual = new double[edps.Length];

        frag.BatchEvaluate(edps, actual);

        for (int i = 0; i < edps.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-7, expected[i] + 1e-7);
    }

    [Fact]
    public void FragilityFunction_BatchIsExceeded_FrequencyMatchesProbability()
    {
        const double edpValue = 0.007;
        var frag = new FragilityFunction(0.005, 0.4);
        double expectedP = frag.Evaluate(edpValue);

        int n = 100_000;
        double[] edps = [.. Enumerable.Repeat(edpValue, n)];
        double[] caps = [.. new MonteCarloSampler().GenerateUniform(n, 1, 42).Cast<double>()];
        bool[] exceeded = new bool[n];

        frag.BatchIsExceeded(edps, caps, exceeded);

        double simP = exceeded.Count(e => e) / (double)n;
        Assert.InRange(simP, expectedP - 0.005, expectedP + 0.005);
    }

    // ─── ComponentFragilitySpec ───────────────────────────────────────────────

    [Fact]
    public void ComponentFragilitySpec_HighEDP_AllLimitStatesExceeded()
    {
        var comp = new Component("C1", new EDP("PID", 1, 1));
        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(0.004, 0.40));
        spec.AddFragilityFunction(new FragilityFunction(0.012, 0.40));
        spec.AddFragilityFunction(new FragilityFunction(0.025, 0.40));

        // At edp=0.050 all three LS have exceedance probability > 0.99.
        // Capacity samples = 0.01 (well below 0.99) → DS3.
        int ds = spec.EvaluateDamageState(0.050, [0.01, 0.01, 0.01]);
        Assert.Equal(3, ds);
    }

    [Fact]
    public void ComponentFragilitySpec_LowEDP_NoDamage()
    {
        var comp = new Component("C1", new EDP("PID", 1, 1));
        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(0.004, 0.40));

        // At edp=0.001, P(DS≥1) ≈ 0.00026.  Capacity sample=0.99 → not exceeded.
        int ds = spec.EvaluateDamageState(0.001, [0.99]);
        Assert.Equal(0, ds);
    }

    [Fact]
    public void ComponentFragilitySpec_ZeroEDP_AlwaysDS0()
    {
        var comp = new Component("C1", new EDP("PID", 1, 1));
        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(0.004, 0.40));
        Assert.Equal(0, spec.EvaluateDamageState(0.0, [0.0]));
    }

    // ─── DamageModel simulation ───────────────────────────────────────────────

    [Fact]
    public void DamageModel_ExceedanceProbability_MatchesFragility()
    {
        const int N = 50_000;
        const double median = 0.005, beta = 0.40, edpValue = 0.007;

        double expected = new FragilityFunction(median, beta).Evaluate(edpValue);

        var comp = new Component("Frame1", new EDP("PID", 1, 1));
        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(median, beta));

        var dm = new DamageModel();
        dm.Add(spec);

        var edpSamples = new double[N];
        Array.Fill(edpSamples, edpValue);
        DamageSample sample = dm.Evaluate(_ => edpSamples, N, seed: 42);

        int exceeded = 0;
        for (int i = 0; i < N; i++)
            if (sample.DamageStates[i, 0] >= 1) exceeded++;

        Assert.InRange(exceeded / (double)N, expected - 0.005, expected + 0.005);
    }

    // ─── DemandModel calibration ───────────────────────────────────────────────

    [Fact]
    public void DemandModel_MLECalibration_RecoversTrueParameters()
    {
        const int N = 5000;
        double muLn = Math.Log(0.005), beta = 0.40;

        var trueDist = new LognormalDistribution(muLn, beta);
        double[] data = trueDist.Sample(N, seed: 99);

        var rawSamples = new double[N, 1];
        for (int i = 0; i < N; i++) rawSamples[i, 0] = data[i];

        var edp = new EDP("PID", 1, 1);
        var dm = new DemandModel();
        dm.CalibrateFromData([edp], rawSamples, EdpDistributionKind.Lognormal);

        EdpDistributionSpec spec = dm.Specs[0];
        Assert.InRange(spec.Theta1, muLn - 0.05, muLn + 0.05);
        Assert.InRange(spec.Theta2, beta - 0.05, beta + 0.05);
    }

    // ─── ConsequenceFunction ──────────────────────────────────────────────────

    [Fact]
    public void ConsequenceFunction_Deterministic_AlwaysReturnsMedian()
    {
        var fn = new ConsequenceFunction(1, DecisionVariable.Cost, 10_000, beta: 0.0);
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
            Assert.Equal(10_000.0, fn.SampleLoss(rng.NextDouble()));
    }

    [Fact]
    public void ConsequenceFunction_Stochastic_MedianIsApproxCorrect()
    {
        const int N = 50_000;
        double median = 15_000, beta = 0.40;
        var fn = new ConsequenceFunction(2, DecisionVariable.Cost, median, beta);

        var rng = new Random(42);
        double[] losses = [.. Enumerable.Range(0, N).Select(_ => fn.SampleLoss(rng.NextDouble()))];
        double sampleMedian = losses.OrderBy(x => x).ToArray()[N / 2];

        Assert.InRange(sampleMedian / median, 0.98, 1.02);
    }

    [Fact]
    public void ConsequenceFunction_ZeroMedian_ReturnsZero()
    {
        var fn = new ConsequenceFunction(0, DecisionVariable.Cost, 0.0);
        Assert.Equal(0.0, fn.SampleLoss(0.5));
    }

    // ─── SimulationPipeline integration ──────────────────────────────────────

    [Fact]
    public void SimulationPipeline_SingleComponent_ProducesPositiveMeanLoss()
    {
        const int N = 2000;

        var edp = new EDP("PID", 1, 1);
        var comp = new Component("Frame1", edp, quantity: 2.0);
        var asset = new Asset("A1", "Test");
        asset.AddComponent(comp);

        var demandModel = new DemandModel();
        demandModel.AddEdp(new EdpDistributionSpec(
            edp, EdpDistributionKind.Lognormal, Math.Log(0.007), 0.40));

        var fragSpec = new ComponentFragilitySpec(comp);
        fragSpec.AddFragilityFunction(new FragilityFunction(0.005, 0.40));

        var damageModel = new DamageModel();
        damageModel.Add(fragSpec);

        var lossModel = new LossModel();
        lossModel.AddConsequence("Frame1",
            new ConsequenceFunction(1, DecisionVariable.Cost, 10_000, beta: 0.30));

        SimulationResult result = new SimulationPipeline(asset)
            .WithDemand(demandModel)
            .WithDamage(damageModel)
            .WithLoss(lossModel)
            .WithSampler(LatinHypercubeSampler.Standard)
            .WithDecisionVariables(DecisionVariable.Cost)
            .Run(N, seed: 1);

        Assert.True(result.MeanCost > 0, $"Expected positive mean cost but got {result.MeanCost}");
        Assert.Equal(N, result.NumberOfSimulations);
    }

    [Fact]
    public void SimulationPipeline_Percentiles_AreOrdered()
    {
        const int N = 2000;

        var edp = new EDP("PID", 1, 1);
        var comp = new Component("C1", edp, quantity: 1.0);
        var asset = new Asset("A1", "Test");
        asset.AddComponent(comp);

        var dm = new DemandModel();
        dm.AddEdp(new EdpDistributionSpec(edp, EdpDistributionKind.Lognormal, Math.Log(0.007), 0.40));

        var frag = new ComponentFragilitySpec(comp);
        frag.AddFragilityFunction(new FragilityFunction(0.005, 0.40));
        var dModel = new DamageModel();
        dModel.Add(frag);

        var lm = new LossModel();
        lm.AddConsequence("C1", new ConsequenceFunction(1, DecisionVariable.Cost, 20_000, 0.4));

        SimulationResult result = new SimulationPipeline(asset)
            .WithDemand(dm).WithDamage(dModel).WithLoss(lm)
            .Run(N, seed: 42);

        double p5 = result.CostAtPercentile(0.05);
        double p50 = result.CostAtPercentile(0.50);
        double p95 = result.CostAtPercentile(0.95);

        Assert.True(p5 <= p50, $"5th pctile {p5} > median {p50}");
        Assert.True(p50 <= p95, $"Median {p50} > 95th pctile {p95}");
    }

    [Fact]
    public void SimulationResult_ExceedanceProbability_IsBetweenZeroAndOne()
    {
        const int N = 1000;
        var edp = new EDP("PID", 1, 1);
        var comp = new Component("C1", edp);
        var asset = new Asset("A1", "Test");
        asset.AddComponent(comp);

        var dm = new DemandModel();
        dm.AddEdp(new EdpDistributionSpec(edp, EdpDistributionKind.Lognormal, Math.Log(0.007), 0.4));

        var spec = new ComponentFragilitySpec(comp);
        spec.AddFragilityFunction(new FragilityFunction(0.005, 0.4));
        var dModel = new DamageModel();
        dModel.Add(spec);

        SimulationResult result = new SimulationPipeline(asset)
            .WithDemand(dm).WithDamage(dModel).WithLoss(new LossModel())
            .Run(N, seed: 7);

        double p = result.GetExceedanceProbability("C1", 1);
        Assert.InRange(p, 0.0, 1.0);
    }
}
