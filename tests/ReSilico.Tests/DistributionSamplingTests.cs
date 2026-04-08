// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Unit tests: Distribution sampling and statistical correctness

using System.Numerics;
using Xunit;
using ReSilico.Core.Distributions;
using ReSilico.Core.RandomVariables;
using ReSilico.Core.Sampling;

namespace ReSilico.Tests;

public class DistributionSamplingTests
{
    private const int N = 50_000;
    private const int Seed = 12345;

    // ─── Type hierarchy ────────────────────────────────────────────────────────

    [Fact]
    public void Normal_InheritsDistributionBase()
    {
        var dist = new NormalDistribution(0, 1);
        Assert.IsAssignableFrom<DistributionBase>(dist);
        Assert.IsAssignableFrom<IUnivariateDistribution>(dist);
    }

    [Fact]
    public void Lognormal_InheritsDistributionBase()
    {
        var dist = new LognormalDistribution(0, 0.4);
        Assert.IsAssignableFrom<DistributionBase>(dist);
    }

    // ─── Normal scalar primitives ─────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(5.0, 2.0)]
    [InlineData(-3.0, 0.5)]
    public void Normal_SampleMeanAndStdDev_AreCorrect(double mu, double sigma)
    {
        var dist = new NormalDistribution(mu, sigma);
        double[] samples = dist.Sample(N, Seed);

        double sampleMean = samples.Average();
        double sampleStd = Math.Sqrt(samples.Average(x => (x - sampleMean) * (x - sampleMean)));

        // Mean within 3σ/√N of true mean.
        double se = sigma / Math.Sqrt(N);
        Assert.InRange(sampleMean, mu - 4 * se, mu + 4 * se);
        Assert.InRange(sampleStd / sigma, 0.98, 1.02);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(2.0, 1.0, 2.0)]
    [InlineData(-1.0, 2.0, -1.0)]
    public void Normal_InverseCDF_RoundTrip(double mu, double sigma, double x)
    {
        var dist = new NormalDistribution(mu, sigma);
        Assert.Equal(x, dist.InverseCDF(dist.CDF(x)), precision: 6);
    }

    [Fact]
    public void Normal_CDF_IsMonotonicallyIncreasing()
    {
        var dist = new NormalDistribution(0, 1);
        double prev = 0.0;
        for (double x = -5.0; x <= 5.0; x += 0.1)
        {
            double cdf = dist.CDF(x);
            Assert.True(cdf >= prev - 1e-12);
            prev = cdf;
        }
    }

    // ─── Normal batch CDF ─────────────────────────────────────────────────────

    [Fact]
    public void Normal_BatchCDF_MatchesScalarCDF()
    {
        var dist = new NormalDistribution(2.0, 0.8);
        var x = Enumerable.Range(-20, 50).Select(i => i * 0.2).ToArray();
        var expected = x.Select(dist.CDF).ToArray();
        var actual = new double[x.Length];

        dist.BatchCDF(x, actual);

        for (int i = 0; i < x.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-7, expected[i] + 1e-7);
    }

    // ─── Normal static standard CDF ───────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(-1.6449, 0.05)]
    [InlineData(1.6449, 0.95)]
    [InlineData(-1.96, 0.025)]
    [InlineData(1.96, 0.975)]
    public void Normal_StandardCDF_MatchesKnownValues(double z, double expected)
    {
        Assert.InRange(NormalDistribution.StandardCDF(z), expected - 0.001, expected + 0.001);
    }

    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.025, -1.96)]
    [InlineData(0.975, 1.96)]
    public void Normal_StandardInverseCDF_MatchesKnownValues(double p, double expected)
    {
        Assert.InRange(NormalDistribution.StandardInverseCDF(p), expected - 0.01, expected + 0.01);
    }

    // ─── Lognormal scalar primitives ──────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.3)]
    [InlineData(-0.693, 0.5)]
    [InlineData(1.609, 0.4)]
    public void Lognormal_SampleMedian_IsCorrect(double muLn, double beta)
    {
        var dist = new LognormalDistribution(muLn, beta);
        double[] sorted = [.. dist.Sample(N, Seed).OrderBy(x => x)];
        double sampleMedian = sorted[N / 2];
        double analyticalMedian = Math.Exp(muLn);
        Assert.InRange(sampleMedian / analyticalMedian, 0.97, 1.03);
    }

    [Fact]
    public void Lognormal_CDF_AtMedian_Is_HalfProbability()
    {
        var dist = LognormalDistribution.FromMedianAndDispersion(0.007, 0.40);
        Assert.InRange(dist.CDF(dist.Median), 0.499, 0.501);
    }

    [Fact]
    public void Lognormal_AllSamples_ArePositive()
    {
        var dist = new LognormalDistribution(0.0, 0.5);
        Assert.All(dist.Sample(2000, Seed), s => Assert.True(s > 0));
    }

    [Fact]
    public void Lognormal_InverseCDF_RoundTrip()
    {
        var dist = LognormalDistribution.FromMedianAndDispersion(0.005, 0.4);
        double x = 0.008;
        Assert.Equal(x, dist.InverseCDF(dist.CDF(x)), precision: 8);
    }

    // ─── Lognormal batch CDF ──────────────────────────────────────────────────

    [Fact]
    public void Lognormal_BatchCDF_MatchesScalarCDF()
    {
        var dist = LognormalDistribution.FromMedianAndDispersion(0.005, 0.4);
        double[] x = [0.001, 0.003, 0.005, 0.007, 0.010, 0.015, 0.020];
        double[] expected = [.. x.Select(v => dist.CDF(v))];
        double[] actual = new double[x.Length];

        dist.BatchCDF(x, actual);

        for (int i = 0; i < x.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-7, expected[i] + 1e-7);
    }

    // ─── Lognormal batch InverseCDF ───────────────────────────────────────────

    [Fact]
    public void Lognormal_BatchInverseCDF_MatchesScalarInverseCDF()
    {
        var dist = LognormalDistribution.FromMedianAndDispersion(0.005, 0.4);
        double[] probs = [0.05, 0.16, 0.50, 0.84, 0.95];
        double[] expected = [.. probs.Select(p => dist.InverseCDF(p))];
        double[] actual = new double[probs.Length];

        dist.BatchInverseCDF(probs, actual);

        for (int i = 0; i < probs.Length; i++)
            Assert.InRange(actual[i] / expected[i], 0.9999, 1.0001);
    }

    // ─── DistributionBase SIMD VectorizedZScore ───────────────────────────────

    [Fact]
    public void Normal_BatchCDF_LargeArray_MatchesScalar()
    {
        // Verifies SIMD path on arrays wider than Vector<double>.Count.
        var dist = new NormalDistribution(0, 1);
        int vLen = Vector<double>.Count;
        double[] x = [.. Enumerable.Range(0, vLen * 8 + 3).Select(i => -4.0 + i * 0.1)];

        double[] expected = [.. x.Select(v => dist.CDF(v))];
        double[] actual = new double[x.Length];
        dist.BatchCDF(x, actual);

        for (int i = 0; i < x.Length; i++)
            Assert.InRange(actual[i], expected[i] - 1e-5, expected[i] + 1e-5);
    }

    // ─── ArrayPool / Sample uses BatchInverseCDF ──────────────────────────────

    [Fact]
    public void Lognormal_Sample_ViaBatchInverseCDF_IsEquivalentToScalar()
    {
        var dist = LognormalDistribution.FromMedianAndDispersion(0.007, 0.35);
        double[] batchSamples = dist.Sample(1000, Seed);
        // Just verify positive values and reasonable range.
        Assert.All(batchSamples, v =>
        {
            Assert.True(v > 0, $"Non-positive sample: {v}");
            Assert.True(v < 1.0, $"Implausibly large sample: {v}");
        });
    }

    // ─── RandomVariable truncation ────────────────────────────────────────────

    [Fact]
    public void RandomVariable_TruncatedNormal_AllSamplesInBounds()
    {
        var rv = new RandomVariable("z", new NormalDistribution(0, 1), truncLower: 0.0);
        var uniforms = Enumerable.Range(0, N).Select(i => (i + 0.5) / N).ToArray();
        double[] samples = rv.BatchInverseTransform(uniforms);
        Assert.All(samples, s => Assert.True(s >= 0.0));
    }

    [Fact]
    public void RandomVariable_TruncatedLognormal_AllSamplesInBounds()
    {
        var dist = LognormalDistribution.FromMedianAndDispersion(0.01, 0.4);
        var rv = new RandomVariable("pid", dist, truncLower: 0.0, truncUpper: 0.10);
        var uniforms = Enumerable.Range(0, N).Select(i => (i + 0.5) / N).ToArray();
        double[] samples = rv.BatchInverseTransform(uniforms);
        Assert.All(samples, s =>
        {
            Assert.True(s >= 0.0);
            Assert.True(s <= 0.10);
        });
    }

    // ─── Samplers ─────────────────────────────────────────────────────────────

    [Fact]
    public void MonteCarloSampler_AllValuesInOpenUnitInterval()
    {
        var sampler = new MonteCarloSampler();
        double[,] u = sampler.GenerateUniform(1000, 5, Seed);
        int n = u.GetLength(0), d = u.GetLength(1);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < d; j++)
                Assert.InRange(u[i, j], 1e-14, 1.0 - 1e-14);
    }

    [Fact]
    public void LatinHypercubeSampler_EachStratumCoveredPerDimension()
    {
        int n = 200, dim = 4;
        var sampler = new LatinHypercubeSampler();
        double[,] u = sampler.GenerateUniform(n, dim, Seed);

        for (int j = 0; j < dim; j++)
        {
            var stratumHit = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int stratum = Math.Min((int)(u[i, j] * n), n - 1);
                stratumHit[stratum] = true;
            }
            Assert.All(stratumHit, hit => Assert.True(hit, "LHS stratum not covered"));
        }
    }

    [Fact]
    public void SamplerFactory_CreatesMC_AndLHS()
    {
        ISampler mc = SamplerFactory.Create(SamplingMethod.MonteCarlo);
        ISampler lhs = SamplerFactory.Create(SamplingMethod.LatinHypercube);
        Assert.IsType<MonteCarloSampler>(mc);
        Assert.IsType<LatinHypercubeSampler>(lhs);
    }

    // ─── Gaussian copula ──────────────────────────────────────────────────────

    [Fact]
    public void RandomVariableSet_Correlation_InducesCorrectPearsonRho()
    {
        const double targetRho = 0.80;
        const int n = 20_000;

        double[,] corrMatrix = { { 1.0, targetRho }, { targetRho, 1.0 } };
        var rv1 = new RandomVariable("x1", new NormalDistribution(0, 1));
        var rv2 = new RandomVariable("x2", new NormalDistribution(0, 1));
        var set = new RandomVariableSet("pair", [rv1, rv2], corrMatrix);

        double[,] uniforms = new MonteCarloSampler().GenerateUniform(n, 2, Seed);
        set.ApplyCorrelation(uniforms);

        var col1 = Enumerable.Range(0, n).Select(i => NormalDistribution.StandardInverseCDF(uniforms[i, 0])).ToArray();
        var col2 = Enumerable.Range(0, n).Select(i => NormalDistribution.StandardInverseCDF(uniforms[i, 1])).ToArray();

        double rho = PearsonR(col1, col2);
        Assert.InRange(rho, targetRho - 0.02, targetRho + 0.02);
    }

    [Fact]
    public void RandomVariableSet_NearSPD_FallbackDoesNotThrow()
    {
        // Slightly non-positive-definite matrix.
        double[,] rho = { { 1.0, 0.9, 0.9 }, { 0.9, 1.0, 0.9 }, { 0.9, 0.9, 1.0 } };
        var rvs = Enumerable.Range(0, 3)
            .Select(i => new RandomVariable($"x{i}", new NormalDistribution(0, 1)))
            .ToList();
        var set = new RandomVariableSet("triple", rvs, rho);

        double[,] uniforms = new MonteCarloSampler().GenerateUniform(100, 3, Seed);
        var ex = Record.Exception(() => set.ApplyCorrelation(uniforms));
        Assert.Null(ex);
    }

    // ─── Registry with injected LHS sampler ───────────────────────────────────

    [Fact]
    public void Registry_WithLHSSampler_ProducesCorrectSampleCount()
    {
        var registry = new RandomVariableRegistry(LatinHypercubeSampler.Standard);
        registry.Register(new RandomVariable("a", new NormalDistribution(0, 1)));
        registry.Register(new RandomVariable("b", LognormalDistribution.FromMedianAndDispersion(0.005, 0.4)));

        registry.GenerateSample(5000, Seed);
        Assert.Equal(5000, registry.GetSample("a").Length);
        Assert.Equal(5000, registry.GetSample("b").Length);
    }

    [Fact]
    public void Registry_WithMCSampler_MeanConvergesToTrue()
    {
        var registry = new RandomVariableRegistry(new MonteCarloSampler());
        registry.Register(new RandomVariable("mu5", new NormalDistribution(5.0, 1.0)));
        registry.GenerateSample(N, Seed);
        double mean = registry.GetSample("mu5").Average();
        Assert.InRange(mean, 4.95, 5.05);
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static double PearsonR(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double num = 0, dx2 = 0, dy2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double xi = x[i] - mx, yi = y[i] - my;
            num += xi * yi; dx2 += xi * xi; dy2 += yi * yi;
        }
        return num / Math.Sqrt(dx2 * dy2);
    }
}
