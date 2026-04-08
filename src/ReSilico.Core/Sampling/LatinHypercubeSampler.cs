// ReSilico – Probabilistic Damage and Loss Assessment Engine
// LatinHypercubeSampler: stratified sampling with full hypercube coverage

using ReSilico.Core.Distributions;
using Numerics.Sampling;

namespace ReSilico.Core.Sampling;

/// <summary>
/// Latin Hypercube Sampling (LHS) for generating stratified uniform U(0,1) samples.
///
/// Properties:
///   • Each dimension is divided into N equal strata [k/N, (k+1)/N], k = 0…N−1.
///   • Exactly one sample falls in each stratum per dimension.
///   • Strata are randomly permuted independently across dimensions — this breaks
///     any unwanted symmetry and prevents lattice artefacts.
///   • A single sample per stratum is placed at a random location within the
///     stratum (standard LHS) or at the midpoint (<see cref="LhsMidpoint"/>).
///   • Unlike pure MC, LHS guarantees that every region of the domain is
///     represented, which dramatically reduces variance for smooth functions.
///
/// Implements <see cref="ISampler"/> for dependency injection.
/// Thread safety: instances are stateless — safe to call concurrently.
/// </summary>
/// <param name="useMidpoints">
/// If true, samples are placed at stratum midpoints (k + 0.5)/N — fully
/// deterministic given the seed.  If false (default), each sample is at a
/// random location within its stratum — stochastic but stratified.
/// </param>
public sealed class LatinHypercubeSampler(bool useMidpoints = false) : ISampler
{
    private readonly bool _useMidpoints = useMidpoints;

    /// <summary>Convenience singleton: standard (random within strata) LHS.</summary>
    public static LatinHypercubeSampler Standard { get; } = new(useMidpoints: false);

    /// <summary>Convenience singleton: midpoint (deterministic within strata) LHS.</summary>
    public static LatinHypercubeSampler Midpoint { get; } = new(useMidpoints: true);

    // -------------------------------------------------------------------------
    // ISampler

    /// <summary>
    /// Generate a [<paramref name="count"/> × <paramref name="dimension"/>] LHS
    /// sample matrix with values in (0, 1).
    ///
    /// Delegates to RMC.Numerics <see cref="LatinHypercube"/> for the actual
    /// stratification, then clamps to the open interval (0, 1) to guard against
    /// InverseCDF boundary issues.
    /// </summary>
    public double[,] GenerateUniform(int count, int dimension, int seed = -1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0, nameof(count));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimension, 0, nameof(dimension));

        double[,] raw = _useMidpoints
            ? LatinHypercube.Median(count, dimension, seed)
            : LatinHypercube.Random(count, dimension, seed);

        // Clamp to open interval: InverseCDF is undefined at 0 and 1.
        Clamp(raw);
        return raw;
    }

    // -------------------------------------------------------------------------
    // Internal helpers

    private static void Clamp(double[,] m)
    {
        int rows = m.GetLength(0), cols = m.GetLength(1);
        const double lo = 1e-14, hi = 1.0 - 1e-14;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                m[i, j] = Math.Clamp(m[i, j], lo, hi);
    }
}
