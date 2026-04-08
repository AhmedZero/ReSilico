using ReSilico.Core.Distributions;

namespace ReSilico.Core.Sampling;

/// <summary>
/// Sampling strategy selector — used as a shorthand in APIs that accept a simple
/// enum rather than injecting a full <see cref="ISampler"/> instance.
///
/// Use <see cref="SamplerFactory.Create"/> to obtain the corresponding
/// <see cref="ISampler"/> implementation.
/// </summary>
public enum SamplingMethod
{
    /// <summary>Independent uniform random samples (Mersenne Twister).</summary>
    MonteCarlo,

    /// <summary>Latin Hypercube Sampling — random placement within strata.</summary>
    LatinHypercube,

    /// <summary>Latin Hypercube Sampling — deterministic midpoint placement.</summary>
    LatinHypercubeMidpoint,
}

/// <summary>Maps <see cref="SamplingMethod"/> to the corresponding <see cref="ISampler"/>.</summary>
public static class SamplerFactory
{
    public static ISampler Create(SamplingMethod method) => method switch
    {
        SamplingMethod.LatinHypercube => LatinHypercubeSampler.Standard,
        SamplingMethod.LatinHypercubeMidpoint => LatinHypercubeSampler.Midpoint,
        _ => new MonteCarloSampler(),
    };
}
