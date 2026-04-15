// ReSilico – Probabilistic Damage and Loss Assessment Engine
// TCopula: multivariate Student's t-Copula with tail dependence

using System.Buffers;
using Numerics.Distributions;
using Numerics.Mathematics.LinearAlgebra;
using ReSilico.Core.Distributions;

namespace ReSilico.Core.Copulas;

/// <summary>
/// Multivariate Student's t-Copula.
///
/// Tail dependence:
///   Unlike the Gaussian copula (λ_U = 0), the t-Copula has positive upper and
///   lower tail dependence coefficients:
///
///     λ_U = 2 · t_{ν+1}(−√((ν+1)(1−ρ)/(1+ρ)))
///
///   This makes the t-Copula appropriate for modelling joint extremes.
///
/// Algorithm applied in <see cref="ApplyCorrelation"/>:
///   For each realisation i:
///     1. z[j]   = Φ⁻¹(u[j])            — to standard normal
///     2. z_c    = L · z                  — Cholesky correlation
///     3. w_i   ~ χ²(ν) via Gamma(2, ν/2) — mixing variable
///     4. t[j]   = z_c[j] / √(w_i/ν)     — scale to t-distributed
///     5. u_c[j] = F_t(t[j], ν)           — marginal t-CDF to uniform
///
/// Parameterisation:
///   • ν > 2   — required for finite variance
///   • Σ       — correlation matrix (not covariance; diagonal = 1)
///
/// Numerical notes:
///   • The scale factor √(w/ν) is clamped to ≥ 1e-10 to prevent division by zero
///     in degenerate samples (extremely unlikely for ν > 2).
///   • chi-squared samples are generated in one batch using the Gamma(2, ν/2)
///     parameterisation via RMC.Numerics' GammaDistribution.
///   • All temporary work is done in rented <see cref="ArrayPool{T}"/> buffers.
///
/// Thread safety: <see cref="ApplyCorrelation"/> is thread-safe; the chi-squared
/// sampler uses an atomic call counter to ensure each invocation is seeded
/// differently while remaining reproducible given the same constructor seed.
/// </summary>
public sealed class TCopula : CopulaBase
{
    private readonly double _nu;               // degrees of freedom
    private readonly GammaDistribution _gamma; // χ²(ν) = Gamma(kappa=ν/2, theta=2)
    private readonly StudentT _studentT;       // standard t(ν) CDF
    private readonly int _seed;
    private int _callCount;                    // incremented atomically per ApplyCorrelation call

    /// <summary>
    /// Create a t-Copula with given degrees of freedom and correlation matrix.
    /// </summary>
    /// <param name="degreesOfFreedom">
    /// ν — degrees of freedom. Must be &gt; 2 (required for finite variance).
    /// As ν → ∞ the t-Copula converges to the Gaussian copula.
    /// </param>
    /// <param name="correlationMatrix">
    /// Symmetric PSD Pearson correlation matrix Σ (diagonal = 1).
    /// </param>
    /// <param name="seed">
    /// Seed for the chi-squared mixing variable sampler.
    /// Positive values give a reproducible, deterministic sequence;
    /// 0 or negative uses the system clock (non-deterministic).
    /// Default: 42.
    /// </param>
    public TCopula(double degreesOfFreedom, double[,] correlationMatrix, int seed = 42)
        : base(correlationMatrix)
    {
        if (degreesOfFreedom <= 2.0)
            throw new ArgumentOutOfRangeException(
                nameof(degreesOfFreedom),
                $"Degrees of freedom must be > 2 for finite variance (got {degreesOfFreedom}).");

        _nu        = degreesOfFreedom;
        _seed      = seed;
        // χ²(ν) = Gamma(scale θ = 2, shape κ = ν/2)
        _gamma     = new GammaDistribution(scale: 2.0, shape: _nu / 2.0);
        // Standard t(ν): location=0, scale=1
        _studentT  = new StudentT(location: 0.0, scale: 1.0, degreesOfFreedom: _nu);
    }

    /// <summary>Degrees of freedom ν.</summary>
    public double DegreesOfFreedom => _nu;

    // ─── Theoretical tail dependence ────────────────────────────────────────���

    /// <summary>
    /// Upper (and lower) tail dependence coefficient for a bivariate margin
    /// with pairwise correlation <paramref name="rho"/>.
    ///
    ///   λ_U = 2 · t_{ν+1}(−√((ν+1)(1−ρ)/(1+ρ)))
    /// </summary>
    public double TailDependence(double rho)
    {
        if (rho >= 1.0) return 1.0;
        if (rho <= -1.0) return 0.0;
        var tNu1 = new StudentT(0.0, 1.0, _nu + 1.0);
        double arg = -Math.Sqrt((_nu + 1.0) * (1.0 - rho) / (1.0 + rho));
        return 2.0 * tNu1.CDF(arg);
    }

    // ─── ApplyCorrelation ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override void ApplyCorrelation(double[,] uniformSamples)
    {
        Matrix L = EnsureCholesky();

        int count = uniformSamples.GetLength(0);
        int n     = uniformSamples.GetLength(1);

        // Draw count chi-squared(ν) samples for the mixing variables.
        // Each invocation of ApplyCorrelation uses a distinct seed to avoid
        // producing the same chi-squared sequence across calls.
        int chiSqSeed = GetNextSeed();
        double[] chiSq = SampleChiSquared(count, chiSqSeed);

        double[] zBuf = ArrayPool<double>.Shared.Rent(n);
        try
        {
            for (int i = 0; i < count; i++)
            {
                // Step 1 → 2: u[j] → z[j] = Φ⁻¹(u[j])
                for (int j = 0; j < n; j++)
                    zBuf[j] = NormalDistribution.StandardInverseCDF(
                        Math.Clamp(uniformSamples[i, j], 1e-14, 1.0 - 1e-14));

                // Step 3: z_c = L · z  (correlated standard normals)
                var zCorr = L.Multiply(new Vector(zBuf[..n]));

                // Step 4: scale = √(w_i / ν);  t[j] = z_c[j] / scale
                double scale = Math.Max(Math.Sqrt(chiSq[i] / _nu), 1e-10);

                // Step 5: u_c[j] = F_t(t[j], ν)
                for (int j = 0; j < n; j++)
                {
                    double t = zCorr[j] / scale;
                    uniformSamples[i, j] = Math.Clamp(_studentT.CDF(t), 1e-14, 1.0 - 1e-14);
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(zBuf);
        }
    }
    private int GetNextSeed()
    {
        int current = System.Threading.Interlocked.Increment(ref _callCount);
        return _seed > 0 ? _seed + current : -1;
    }
    // ─── Chi-squared sampler ─────────────────────────────────────────────────

    /// <summary>
    /// Sample <paramref name="count"/> values from χ²(ν) using
    /// the Gamma parameterisation:  χ²(ν) = Gamma(shape = ν/2, scale = 2).
    /// </summary>
    private double[] SampleChiSquared(int count, int seed)
    {
        // GenerateRandomValues returns double[] via the UnivariateDistributionBase method.
        // The seed convention in RMC.Numerics: ≤0 → use system clock.
        return _gamma.GenerateRandomValues(count, seed > 0 ? seed : -1);
    }
}
