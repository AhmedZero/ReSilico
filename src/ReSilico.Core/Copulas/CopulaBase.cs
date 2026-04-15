// ReSilico – Probabilistic Damage and Loss Assessment Engine
// CopulaBase: shared Cholesky decomposition logic for correlation-based copulas

using Numerics.Mathematics.LinearAlgebra;

namespace ReSilico.Core.Copulas;

/// <summary>
/// Abstract base for copulas that use a Cholesky-factored correlation matrix Σ.
///
/// Responsibilities:
///   • Validate the correlation matrix on construction.
///   • Lazily compute and cache the lower Cholesky factor L (thread-safe).
///   • Fall back to the nearest SPD approximation (Higham 1988) if Σ is not PD.
///
/// Concrete subclasses implement <see cref="ICopula.ApplyCorrelation"/> and call
/// <see cref="EnsureCholesky"/> to obtain the cached <see cref="CholeskyL"/>.
/// </summary>
public abstract class CopulaBase : ICopula
{
    protected readonly double[,] _rho;
    private readonly Lazy<Matrix> _choleskyL;

    protected CopulaBase(double[,] correlationMatrix)
    {
        ArgumentNullException.ThrowIfNull(correlationMatrix);
        int n = correlationMatrix.GetLength(0);
        if (correlationMatrix.GetLength(1) != n)
            throw new ArgumentException("Correlation matrix must be square.");
        _rho = correlationMatrix;
        _choleskyL = new Lazy<Matrix>(() => ComputeCholesky(_rho));

    }

    public int Dimension => _rho.GetLength(0);

    /// <summary>The Pearson correlation matrix Σ supplied at construction.</summary>
    public double[,] CorrelationMatrix => _rho;

    /// <inheritdoc/>
    public abstract void ApplyCorrelation(double[,] uniformSamples);

    // ─── Protected access to the Cholesky factor ──────────────────────────────

    /// <summary>
    /// Ensure the Cholesky factor is computed and return it.
    /// Uses a double-checked lock for thread-safe lazy initialisation.
    /// </summary>
    protected Matrix EnsureCholesky() => _choleskyL.Value;

    // ─── Cholesky helpers (shared between all copula subclasses) ─────────────

    private static Matrix ComputeCholesky(double[,] rho)
    {
        var rhoMatrix = new Matrix(rho);
        var chol = new CholeskyDecomposition(rhoMatrix);
        if (chol.IsPositiveDefinite) return chol.L;

        // Nearest positive-definite approximation (Higham 1988 / SVD).
        return NearestSPD_Cholesky(rhoMatrix);
    }

    private static Matrix NearestSPD_Cholesky(Matrix A)
    {
        var svd = new SingularValueDecomposition(A);
        int n = A.NumberOfRows;

        var diagSigma = new Matrix(n, n);
        for (int i = 0; i < n; i++)
            diagSigma[i, i] = Math.Max(svd.W[i], 1e-10);

        // A_nearest = U · |Σ| · Uᵀ
        var nearest = svd.U.Multiply(diagSigma).Multiply(Matrix.Transpose(svd.U));

        // Symmetrise to eliminate floating-point asymmetry.
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
    protected void ValidateInputDimensions(double[,] uniformSamples)
    {
        ArgumentNullException.ThrowIfNull(uniformSamples);
        int inputDim = uniformSamples.GetLength(1);
        if (inputDim != Dimension)
            throw new ArgumentException(
                $"Input dimension {inputDim} does not match copula dimension {Dimension}.");
    }
}
