// ReSilico – Probabilistic Damage and Loss Assessment Engine
// RandomVariable: named uncertain quantity with distribution + truncation

using System.Buffers;
using ReSilico.Core.Distributions;

namespace ReSilico.Core.RandomVariables;

/// <summary>
/// A named univariate uncertain quantity defined by a probability distribution
/// and optional lower/upper truncation limits.
///
/// Truncated inverse-transform:
///   u_adj = F(a) + u · (F(b) − F(a))
///   x     = F⁻¹(u_adj)
/// where a, b are truncation limits (NaN = unbounded).
///
/// Performance:
///   • <see cref="BatchInverseTransform"/> uses the distribution's vectorized
///     <see cref="DistributionBase.BatchInverseCDF"/> when the underlying object
///     derives from <see cref="DistributionBase"/>.
///   • All temporary buffers are rented from <see cref="ArrayPool{T}"/>.
///   • Fields are readonly — effectively immutable after construction.
/// </summary>
public sealed class RandomVariable
{
    private readonly IUnivariateDistribution _distribution;
    private readonly double _truncLower;
    private readonly double _truncUpper;
    private readonly double _cdfLower;    // F(truncLower), or 0 if no lower truncation
    private readonly double _cdfRange;    // F(truncUpper) − F(truncLower)
    private readonly bool _isTruncated;

    public RandomVariable(
        string name,
        IUnivariateDistribution distribution,
        double truncLower = double.NaN,
        double truncUpper = double.NaN)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(distribution);

        Name = name;
        _distribution = distribution;
        _truncLower = truncLower;
        _truncUpper = truncUpper;

        double cdfLo = double.IsNaN(truncLower) ? 0.0 : distribution.CDF(truncLower);
        double cdfHi = double.IsNaN(truncUpper) ? 1.0 : distribution.CDF(truncUpper);

        if (cdfHi <= cdfLo)
            throw new ArgumentException(
                $"Truncation range [{truncLower}, {truncUpper}] has zero or negative probability mass.");

        _cdfLower = cdfLo;
        _cdfRange = cdfHi - cdfLo;
        _isTruncated = !double.IsNaN(truncLower) || !double.IsNaN(truncUpper);
    }

    // ── Properties ─────────────────────────────────────────────────────────

    public string Name { get; }
    public IUnivariateDistribution Distribution => _distribution;
    public bool HasLowerTruncation => !double.IsNaN(_truncLower);
    public bool HasUpperTruncation => !double.IsNaN(_truncUpper);

    // ── Scalar sampling ─────────────────────────────────────────────────────

    /// <summary>
    /// Apply the (truncated) inverse-CDF to a single uniform U(0,1) variate.
    /// </summary>
    public double InverseTransform(double u)
    {
        double uAdj = _isTruncated
            ? Math.Clamp(_cdfLower + u * _cdfRange, 1e-14, 1.0 - 1e-14)
            : Math.Clamp(u, 1e-14, 1.0 - 1e-14);
        return _distribution.InverseCDF(uAdj);
    }

    // ── Batch sampling — uses ArrayPool and vectorized BatchInverseCDF ────────

    /// <summary>
    /// Apply the (truncated) inverse-CDF to a batch of uniform U(0,1) samples.
    ///
    /// If the underlying distribution is a <see cref="DistributionBase"/> subclass,
    /// the vectorized <see cref="DistributionBase.BatchInverseCDF"/> is used.
    /// A rented <see cref="ArrayPool{T}"/> buffer holds the adjusted probabilities.
    /// </summary>
    public double[] BatchInverseTransform(ReadOnlySpan<double> uniformSamples)
    {
        int n = uniformSamples.Length;
        var result = new double[n];

        if (!_isTruncated)
        {
            // No truncation: apply BatchInverseCDF directly if available.
            if (_distribution is DistributionBase db)
            {
                db.BatchInverseCDF(uniformSamples, result);
                return result;
            }
            // Scalar fallback.
            for (int i = 0; i < n; i++)
                result[i] = _distribution.InverseCDF(Math.Clamp(uniformSamples[i], 1e-14, 1.0 - 1e-14));
            return result;
        }

        // Truncated: adjust probabilities first.
        double[] adjBuf = ArrayPool<double>.Shared.Rent(n);
        try
        {
            for (int i = 0; i < n; i++)
                adjBuf[i] = Math.Clamp(_cdfLower + uniformSamples[i] * _cdfRange, 1e-14, 1.0 - 1e-14);

            if (_distribution is DistributionBase db2)
                db2.BatchInverseCDF(adjBuf.AsSpan(0, n), result);
            else
                for (int i = 0; i < n; i++)
                    result[i] = _distribution.InverseCDF(adjBuf[i]);

            return result;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(adjBuf);
        }
    }

    // ── InverseTransform overload accepting Span (legacy compat) ────────────

    /// <inheritdoc cref="BatchInverseTransform(ReadOnlySpan{double})"/>
    public double[] InverseTransform(ReadOnlySpan<double> uniformSamples) =>
        BatchInverseTransform(uniformSamples);

    // ── Probability queries ─────────────────────────────────────────────────

    public double PDF(double x) => _distribution.PDF(x) / _cdfRange;
    public double CDF(double x) =>
        Math.Clamp((_distribution.CDF(x) - _cdfLower) / _cdfRange, 0.0, 1.0);
}
