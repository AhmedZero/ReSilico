// ReSilico – Probabilistic Damage and Loss Assessment Engine
// RandomVariableSet: correlated group of RVs via Gaussian copula + ArrayPool

using System.Buffers;
using Numerics.Mathematics.LinearAlgebra;
using ReSilico.Core.Distributions;

namespace ReSilico.Core.RandomVariables;

/// <summary>
/// A named group of <see cref="RandomVariable"/> instances connected by a
/// Gaussian copula with a supplied Pearson correlation matrix.
///
/// Gaussian copula algorithm applied in <see cref="ApplyCorrelation"/>:
///   1. u[i]    — independent U(0,1)  (input, modified in-place)
///   2. z[i]    = Φ⁻¹(u[i])          — transform to standard normal
///   3. z_c     = L · z               — apply Cholesky factor for correlations
///   4. u_c[i]  = Φ(z_c[i])          — transform back to correlated uniforms
///
/// Numerical robustness:
///   • If the correlation matrix is not positive-definite, the nearest SPD
///     approximation is computed via SVD (Higham 1988).
///   • All temporary work is done in rented <see cref="ArrayPool{T}"/> buffers.
///   • The Cholesky factor is computed once and cached (lazy initialisation).
///
/// Thread safety: <see cref="ApplyCorrelation"/> is thread-safe once
/// <see cref="EnsureCholesky"/> has been called (double-checked lock).
/// </summary>
public sealed class RandomVariableSet
{
    private readonly RandomVariable[] _variables;
    private readonly double[,] _rho;
    private volatile Matrix? _choleskyL;  // Lazily initialised, volatile for visibility.
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif
    public RandomVariableSet(
        string name,
        IReadOnlyList<RandomVariable> variables,
        double[,] correlationMatrix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(correlationMatrix);

        int n = variables.Count;
        if (correlationMatrix.GetLength(0) != n || correlationMatrix.GetLength(1) != n)
            throw new ArgumentException($"Correlation matrix must be {n}×{n}.");

        Name = name;
        _variables = [.. variables];
        _rho = correlationMatrix;
    }

    public string Name { get; }
    public int Count => _variables.Length;
    public IReadOnlyList<RandomVariable> Variables => _variables;

    // ─── Gaussian copula ──────────────────────────────────────────────────────

    /// <summary>
    /// Transform a [count × n] matrix of independent U(0,1) samples so that
    /// the columns honour the Gaussian copula with correlation matrix Rho.
    /// Operates in-place on <paramref name="uniformSamples"/>.
    /// </summary>
    public void ApplyCorrelation(double[,] uniformSamples)
    {
        EnsureCholesky();

        int count = uniformSamples.GetLength(0);
        int n = _variables.Length;

        // Rent one buffer for z values per realisation.
        double[] zBuf = ArrayPool<double>.Shared.Rent(n);
        try
        {
            for (int i = 0; i < count; i++)
            {
                // Step 2: u → z via Φ⁻¹
                for (int j = 0; j < n; j++)
                    zBuf[j] = NormalDistribution.StandardInverseCDF(
                        Math.Clamp(uniformSamples[i, j], 1e-14, 1.0 - 1e-14));

                // Step 3: z_c = L · z
                var zCorr = _choleskyL!.Multiply(new Vector(zBuf[..n]));

                // Step 4: z_c → u_c via Φ
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

    // ─── Cholesky initialisation (double-checked lock) ────────────────────────

    private void EnsureCholesky()
    {
        if (_choleskyL is not null) return;
        lock (_lock)
        {
            if (_choleskyL is not null) return;
            _choleskyL = ComputeCholesky(_rho);
        }
    }

    private static Matrix ComputeCholesky(double[,] rho)
    {
        var rhoMatrix = new Matrix(rho);
        var chol = new CholeskyDecomposition(rhoMatrix);
        if (chol.IsPositiveDefinite) return chol.L;

        // Nearest positive-definite approximation (SVD / Higham 1988).
        return NearestSPD_Cholesky(rhoMatrix);
    }

    private static Matrix NearestSPD_Cholesky(Matrix A)
    {
        var svd = new SingularValueDecomposition(A);
        int n = A.NumberOfRows;

        // Build |Σ| diagonal (non-negative singular values).
        var diagSigma = new Matrix(n, n);
        for (int i = 0; i < n; i++)
            diagSigma[i, i] = Math.Max(svd.W[i], 1e-10);

        // A_nearest = U · |Σ| · Uᵀ
        var nearest = svd.U.Multiply(diagSigma).Multiply(Matrix.Transpose(svd.U));

        // Symmetrise (eliminate floating-point asymmetry).
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double avg = 0.5 * (nearest[i, j] + nearest[j, i]);
                nearest[i, j] = avg;
                nearest[j, i] = avg;
            }

        var chol2 = new CholeskyDecomposition(nearest);
        if (!chol2.IsPositiveDefinite)
            throw new InvalidOperationException(
                "Could not find a positive-definite approximation for the correlation matrix.");

        return chol2.L;
    }
}
