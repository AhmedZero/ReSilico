// ReSilico – Probabilistic Damage and Loss Assessment Engine
// GaussianCopula: Gaussian (normal) copula via Cholesky + Φ transform

using System.Buffers;
using Numerics.Mathematics.LinearAlgebra;
using ReSilico.Core.Distributions;

namespace ReSilico.Core.Copulas;

/// <summary>
/// Gaussian (normal) copula.
///
/// Algorithm applied in <see cref="ApplyCorrelation"/>:
///   1. u[i]   — independent U(0,1)            (input)
///   2. z[j]   = Φ⁻¹(u[j])                    (to standard normal)
///   3. z_c    = L · z                          (Cholesky correlation)
///   4. u_c[j] = Φ(z_c[j])                     (back to correlated uniform)
///
/// Numerical notes:
///   • Input uniforms are clamped to (1e-14, 1-1e-14) before Φ⁻¹ to avoid ±∞.
///   • Cholesky is computed once lazily; if the matrix is not PD the nearest
///     SPD approximation is used (inherited from <see cref="CopulaBase"/>).
///   • All temporary work uses rented <see cref="ArrayPool{T}"/> buffers.
/// </summary>
public sealed class GaussianCopula : CopulaBase
{
    /// <param name="correlationMatrix">
    /// Symmetric, positive-semidefinite Pearson correlation matrix Σ (diagonal = 1).
    /// </param>
    public GaussianCopula(double[,] correlationMatrix)
        : base(correlationMatrix) { }

    /// <inheritdoc/>
    public override void ApplyCorrelation(double[,] uniformSamples)
    {
        Matrix L = EnsureCholesky();

        int count = uniformSamples.GetLength(0);
        int n     = uniformSamples.GetLength(1);

        double[] zBuf = ArrayPool<double>.Shared.Rent(n);
        try
        {
            for (int i = 0; i < count; i++)
            {
                // Step 1 → Step 2: u[j] → z[j] = Φ⁻¹(u[j])
                for (int j = 0; j < n; j++)
                    zBuf[j] = NormalDistribution.StandardInverseCDF(
                        Math.Clamp(uniformSamples[i, j], 1e-14, 1.0 - 1e-14));

                // Step 3: z_c = L · z
                var zCorr = L.Multiply(new Vector(zBuf[..n]));

                // Step 4: u_c[j] = Φ(z_c[j])
                for (int j = 0; j < n; j++)
                    uniformSamples[i, j] = Math.Clamp(
                        NormalDistribution.StandardCDF(zCorr[j]), 1e-14, 1.0 - 1e-14);
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(zBuf);
        }
    }
}
