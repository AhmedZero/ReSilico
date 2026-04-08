// ReSilico – Probabilistic Damage and Loss Assessment Engine
// DistributionBase: abstract base for all univariate probability distributions

using System.Buffers;
using System.Numerics;
using Numerics.Sampling;

namespace ReSilico.Core.Distributions;

/// <summary>
/// Abstract base class for all univariate probability distributions.
///
/// Design principles:
///   • Subclasses implement <see cref="PDF"/>, <see cref="CDF"/>, and
///     <see cref="InverseCDF"/> for scalar evaluation.
///   • Batch methods (<see cref="BatchCDF"/>, <see cref="BatchInverseCDF"/>,
///     <see cref="BatchSample"/>) have default scalar implementations but may
///     be overridden with SIMD or otherwise vectorized code.
///   • <see cref="Sample"/> is concrete and delegates to <see cref="BatchInverseCDF"/>
///     so subclasses automatically inherit efficient batch sampling.
///   • All objects are effectively immutable — parameters are set at construction.
/// </summary>
public abstract class DistributionBase : IUnivariateDistribution
{
    // -------------------------------------------------------------------------
    // Abstract core — subclasses must implement these three primitives.

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract double Mean { get; }

    /// <inheritdoc/>
    public abstract double Variance { get; }

    /// <inheritdoc/>
    public double StandardDeviation => Math.Sqrt(Variance);

    /// <summary>Probability density function at <paramref name="x"/>.</summary>
    public abstract double PDF(double x);

    /// <summary>Cumulative distribution function F(x) = P(X ≤ x).</summary>
    public abstract double CDF(double x);

    /// <summary>Inverse CDF (quantile function): returns x such that F(x) = p.</summary>
    public abstract double InverseCDF(double probability);

    /// <inheritdoc/>
    public abstract IUnivariateDistribution Clone();

    // -------------------------------------------------------------------------
    // Concrete batch operations — override in subclasses for SIMD speedups.

    /// <summary>
    /// Evaluate CDF for a batch of inputs.
    /// Writes into <paramref name="result"/> which must be at least as long as
    /// <paramref name="x"/>.
    /// Default: scalar loop — override with SIMD where available.
    /// </summary>
    public virtual void BatchCDF(ReadOnlySpan<double> x, Span<double> result)
    {
        for (int i = 0; i < x.Length; i++)
            result[i] = CDF(x[i]);
    }

    /// <summary>
    /// Apply the inverse CDF to a batch of uniform probabilities.
    /// Writes into <paramref name="result"/>.
    /// Default: scalar loop — override for SIMD acceleration.
    /// </summary>
    public virtual void BatchInverseCDF(ReadOnlySpan<double> probabilities, Span<double> result)
    {
        for (int i = 0; i < probabilities.Length; i++)
            result[i] = InverseCDF(probabilities[i]);
    }

    /// <summary>
    /// Generate samples by combining uniform random generation with
    /// <see cref="BatchInverseCDF"/> (inverse-transform method).
    /// Uses <see cref="ArrayPool{T}"/> for temporary uniform storage.
    /// </summary>
    public virtual double[] Sample(int count, int seed = -1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0, nameof(count));

        // Rent a temporary buffer for uniform samples.
        double[] uniformBuf = ArrayPool<double>.Shared.Rent(count);
        try
        {
            FillUniform(uniformBuf.AsSpan(0, count), seed);
            var result = new double[count];
            BatchInverseCDF(uniformBuf.AsSpan(0, count), result);
            return result;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(uniformBuf);
        }
    }

    // -------------------------------------------------------------------------
    // SIMD helper: vectorized z-score   z = (x − a) × bRecip
    //   where a = shift, bRecip = 1 / scale
    // Used by Normal and Lognormal to vectorize the linear part of the CDF.

    /// <summary>
    /// Compute <c>(x[i] − shift) * scaleRecip</c> for all i in parallel
    /// using hardware SIMD when available.
    /// </summary>
    protected static void VectorizedZScore(
        ReadOnlySpan<double> x,
        Span<double> z,
        double shift,
        double scaleRecip)
    {
        int i = 0;

        if (Vector.IsHardwareAccelerated)
        {
            int vLen = Vector<double>.Count;
            var vShift = new Vector<double>(shift);
            var vScale = new Vector<double>(scaleRecip);

            for (; i <= x.Length - vLen; i += vLen)
            {
                var vx = new Vector<double>(x[i..]);
                var vz = (vx - vShift) * vScale;
                vz.CopyTo(z[i..]);
            }
        }

        // Scalar tail (or full loop when SIMD unavailable).
        for (; i < x.Length; i++)
            z[i] = (x[i] - shift) * scaleRecip;
    }

    // -------------------------------------------------------------------------
    // Private: fill a span with uniform U(0,1) samples.

    private static void FillUniform(Span<double> buffer, int seed)
    {
        var rng = seed >= 0 ? new MersenneTwister(seed) : new MersenneTwister();
        for (int i = 0; i < buffer.Length; i++)
        {
            double u;
            do { u = rng.NextDouble(); } while (u <= 0.0 || u >= 1.0);
            buffer[i] = u;
        }
    }
}
