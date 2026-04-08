// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Distribution contracts

namespace ReSilico.Core.Distributions;

/// <summary>
/// Contract for a univariate probability distribution.
/// Concrete distributions inherit from <see cref="DistributionBase"/> which
/// provides efficient default batch implementations on top of these primitives.
/// </summary>
public interface IUnivariateDistribution
{
    /// <summary>Distribution type name.</summary>
    string Name { get; }

    double Mean { get; }
    double Variance { get; }
    double StandardDeviation { get; }

    double PDF(double x);
    double CDF(double x);
    double InverseCDF(double probability);

    /// <summary>
    /// Generate <paramref name="count"/> independent random samples.
    /// <paramref name="seed"/> = −1 means clock-based (non-deterministic).
    /// </summary>
    double[] Sample(int count, int seed = -1);

    /// <summary>
    /// Evaluate CDF for a batch of inputs, writing into <paramref name="result"/>.
    /// Implementations may use SIMD for performance.
    /// </summary>
    void BatchCDF(ReadOnlySpan<double> x, Span<double> result);

    /// <summary>
    /// Apply the inverse CDF to a batch of probabilities, writing into
    /// <paramref name="result"/>.
    /// </summary>
    void BatchInverseCDF(ReadOnlySpan<double> probabilities, Span<double> result);

    IUnivariateDistribution Clone();
}

/// <summary>Multivariate probability distribution contract.</summary>
public interface IMultivariateDistribution
{
    string Name { get; }
    int Dimension { get; }
    double[] Mean { get; }
    double[,] Covariance { get; }

    double PDF(double[] x);

    /// <summary>
    /// Generate <paramref name="count"/> samples.
    /// Returns a [count × Dimension] array — row i is one realisation.
    /// </summary>
    double[,] Sample(int count, int seed = -1);
}

/// <summary>
/// Marks a sampler that can produce uniform U(0,1) sample matrices.
/// </summary>
public interface ISampler
{
    /// <summary>
    /// Generate a [<paramref name="count"/> × <paramref name="dimension"/>] matrix of
    /// uniform U(0,1) samples.
    /// </summary>
    double[,] GenerateUniform(int count, int dimension, int seed = -1);
}
