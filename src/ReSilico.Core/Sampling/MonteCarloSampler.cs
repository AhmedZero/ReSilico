// ReSilico – Probabilistic Damage and Loss Assessment Engine
// MonteCarloSampler: independent uniform sampling via Mersenne Twister

using ReSilico.Core.Distributions;
using Numerics.Sampling;

namespace ReSilico.Core.Sampling;

/// <summary>
/// Generates independent uniform U(0,1) samples using the Mersenne Twister PRNG.
/// Implements <see cref="ISampler"/> for dependency-injection into
/// <see cref="ReSilico.Core.RandomVariables.RandomVariableRegistry"/>.
///
/// Thread safety: instances are NOT thread-safe — each thread or simulation
/// should use its own instance (or derive child seeds from the master seed).
/// </summary>
public sealed class MonteCarloSampler : ISampler
{
    // -------------------------------------------------------------------------
    // ISampler implementation

    /// <summary>
    /// Generate a [<paramref name="count"/> × <paramref name="dimension"/>] matrix
    /// of independent U(0,1) samples.  Values are strictly in (0, 1) — no 0 or 1
    /// is returned so that InverseCDF calls are always well-defined.
    /// </summary>
    public double[,] GenerateUniform(int count, int dimension, int seed = -1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0, nameof(count));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimension, 0, nameof(dimension));

        var rng = seed >= 0 ? new MersenneTwister(seed) : new MersenneTwister();
        var result = new double[count, dimension];

        for (int i = 0; i < count; i++)
            for (int j = 0; j < dimension; j++)
            {
                double u;
                do { u = rng.NextDouble(); } while (u <= 0.0 || u >= 1.0);
                result[i, j] = u;
            }

        return result;
    }

    // -------------------------------------------------------------------------
    // Static utility methods (convenience — no allocation overhead of creating an instance)

    /// <summary>Extract column <paramref name="j"/> from a [n × d] sample matrix.</summary>
    public static double[] GetColumn(double[,] samples, int j)
    {
        int n = samples.GetLength(0);
        var col = new double[n];
        for (int i = 0; i < n; i++) col[i] = samples[i, j];
        return col;
    }

    /// <summary>Write <paramref name="values"/> into column <paramref name="j"/> of a matrix.</summary>
    public static void SetColumn(double[,] samples, int j, ReadOnlySpan<double> values)
    {
        int n = samples.GetLength(0);
        for (int i = 0; i < n; i++) samples[i, j] = values[i];
    }
}
