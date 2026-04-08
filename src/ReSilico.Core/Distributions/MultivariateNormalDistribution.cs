// ReSilico – Probabilistic Damage and Loss Assessment Engine
// Multivariate Normal distribution wrapper over RMC.Numerics

using Numerics.Distributions;

namespace ReSilico.Core.Distributions;

/// <summary>
/// Multivariate Normal distribution N(μ, Σ).
/// Wraps <see cref="MultivariateNormal"/> from RMC.Numerics.
/// </summary>
public sealed class MultivariateNormalDistribution : IMultivariateDistribution
{
    private readonly MultivariateNormal _inner;

    public MultivariateNormalDistribution(double[] mean, double[,] covariance)
    {
        ArgumentNullException.ThrowIfNull(mean);
        ArgumentNullException.ThrowIfNull(covariance);
        if (mean.Length != covariance.GetLength(0) || mean.Length != covariance.GetLength(1))
            throw new ArgumentException("Mean vector length must match covariance matrix dimensions.");
        _inner = new MultivariateNormal(mean, covariance);
    }

    /// <summary>Create with identity covariance (independent standard normals).</summary>
    public MultivariateNormalDistribution(int dimension)
        : this(new double[dimension], BuildIdentity(dimension)) { }

    public string Name => "MultivariateNormal";
    public int Dimension => _inner.Dimension;
    public double[] Mean => _inner.Mean;
    public double[,] Covariance => _inner.Covariance;

    public double PDF(double[] x) => _inner.PDF(x);

    /// <summary>
    /// Generate <paramref name="count"/> samples.
    /// Returns a [count, Dimension] array — each row is one realisation.
    /// </summary>
    public double[,] Sample(int count, int seed = -1) =>
        _inner.GenerateRandomValues(count, seed);

    /// <summary>Latin-Hypercube samples of the multivariate normal.</summary>
    public double[,] SampleLHS(int count, int seed = -1) =>
        _inner.LatinHypercubeRandomValues(count, seed);

    private static double[,] BuildIdentity(int n)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1.0;
        return m;
    }
}
