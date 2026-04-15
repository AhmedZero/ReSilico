// ReSilico – Probabilistic Damage and Loss Assessment Engine
// DemandModel: EDP distribution management with injected ISampler

using Numerics.Distributions;
using ReSilico.Core.Copulas;
using ReSilico.Core.Distributions;
using ReSilico.Core.RandomVariables;
using ReSilico.Core.Sampling;
using ReSilico.Domain;

namespace ReSilico.Analysis.Demand;

/// <summary>Marginal distribution family for an EDP.</summary>
public enum EdpDistributionKind { Normal, Lognormal }

/// <summary>
/// Specification of a single EDP's marginal distribution.
/// Immutable record — constructed once, never mutated.
/// </summary>
public sealed record EdpDistributionSpec(
    EDP Edp,
    EdpDistributionKind Kind,
    double Theta1,            // Normal: mean μ.  Lognormal: μ_ln = ln(median).
    double Theta2,            // Normal: std-dev σ.  Lognormal: dispersion β.
    double TruncLower = double.NaN,
    double TruncUpper = double.NaN);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages EDP (Engineering Demand Parameter) distributions.
///
/// Responsibilities:
///   1. Accept user-specified distribution parameters or fit them to raw data.
///   2. Optionally accept a Pearson correlation matrix (Gaussian copula).
///   3. Generate correlated EDP samples via a configurable <see cref="ISampler"/>.
///
/// After <see cref="GenerateSample"/> the realisations are accessible via
/// <see cref="GetEdpSample"/>.
/// </summary>
/// <param name="sampler">
/// Uniform sampling strategy for generating EDP samples.
/// Defaults to <see cref="LatinHypercubeSampler.Standard"/>.
/// </param>
public sealed class DemandModel(ISampler? sampler = null)
{
    private readonly List<EdpDistributionSpec> _specs = [];
    private double[,]? _correlationMatrix;
    private ICopula? _copula;
    private RandomVariableRegistry? _registry;
    private readonly ISampler _sampler = sampler ?? LatinHypercubeSampler.Standard;

    // ─── Configuration ────────────────────────────────────────────────────────

    /// <summary>Add a manually specified EDP marginal distribution.</summary>
    public void AddEdp(EdpDistributionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (_specs.Any(s => s.Edp.Key == spec.Edp.Key))
            throw new InvalidOperationException($"EDP '{spec.Edp.Key}' already registered.");
        _specs.Add(spec);
    }

    /// <summary>
    /// Fit marginal lognormal or normal distributions to raw EDP data via
    /// MLE (sample mean and std of log/linear space).
    ///
    /// <paramref name="rawSamples"/> is [N_realisations × N_edps].
    /// </summary>
    public void CalibrateFromData(
        IReadOnlyList<EDP> edps,
        double[,] rawSamples,
        EdpDistributionKind kind = EdpDistributionKind.Lognormal)
    {
        ArgumentNullException.ThrowIfNull(edps);
        ArgumentNullException.ThrowIfNull(rawSamples);

        int nEdps = edps.Count;
        int nSamples = rawSamples.GetLength(0);
        if (rawSamples.GetLength(1) != nEdps)
            throw new ArgumentException($"rawSamples must have {nEdps} columns.");

        _specs.Clear();
        for (int j = 0; j < nEdps; j++)
        {
            var col = new double[nSamples];
            for (int i = 0; i < nSamples; i++) col[i] = rawSamples[i, j];
            (double t1, double t2) = FitMarginal(col, kind);
            _specs.Add(new EdpDistributionSpec(edps[j], kind, t1, t2));
        }
    }

    /// <summary>
    /// Set a Pearson correlation matrix for correlated EDP sampling (Gaussian copula).
    /// Must be n×n where n = number of registered EDPs.
    /// </summary>
    public void SetCorrelation(double[,] correlationMatrix)
    {
        ArgumentNullException.ThrowIfNull(correlationMatrix);
        int n = _specs.Count;
        if (correlationMatrix.GetLength(0) != n || correlationMatrix.GetLength(1) != n)
            throw new ArgumentException($"Correlation matrix must be {n}×{n}.");
        _correlationMatrix = correlationMatrix;
        _copula = null; // matrix overrides a previously set copula
    }

    /// <summary>
    /// Plug in an explicit copula (e.g. <see cref="TCopula"/>) for correlated EDP sampling.
    /// Takes precedence over <see cref="SetCorrelation"/> if both are called.
    /// </summary>
    public void SetCopula(ICopula copula)
    {
        ArgumentNullException.ThrowIfNull(copula);
        _copula = copula;
        _correlationMatrix = null; // copula overrides a previously set matrix
    }

    // ─── Sample generation ────────────────────────────────────────────────────

    /// <summary>
    /// Generate <paramref name="count"/> correlated EDP realisations.
    /// Inject a custom <paramref name="samplerOverride"/> to override the
    /// sampler configured at construction time.
    /// </summary>
    public void GenerateSample(int count, int seed = -1, ISampler? samplerOverride = null)
    {
        if (_specs.Count == 0)
            throw new InvalidOperationException("No EDP distributions defined.");

        var activeSampler = samplerOverride ?? _sampler;
        _registry = new RandomVariableRegistry(activeSampler);

        foreach (var spec in _specs)
        {
            ReSilico.Core.Distributions.IUnivariateDistribution dist =
                spec.Kind == EdpDistributionKind.Normal
                ? new NormalDistribution(spec.Theta1, spec.Theta2)
                : new LognormalDistribution(spec.Theta1, spec.Theta2);

            _registry.Register(new RandomVariable(
                spec.Edp.Key, dist, spec.TruncLower, spec.TruncUpper));
        }

        ICopula? activeCopula = _copula
            ?? (_correlationMatrix is not null ? new GaussianCopula(_correlationMatrix) : null);

        if (activeCopula is not null)
        {
            var rvList = _specs.Select(s => _registry.Get(s.Edp.Key)).ToList();
            _registry.RegisterSet(new RandomVariableSet("EDPs", rvList, activeCopula));
        }

        _registry.GenerateSample(count, seed);
    }

    // ─── Sample access ────────────────────────────────────────────────────────

    public double[] GetEdpSample(EDP edp) => GetEdpSample(edp.Key);

    public double[] GetEdpSample(string edpKey)
    {
        EnsureSampled();
        return _registry!.GetSample(edpKey);
    }

    public int SampleCount => _registry?.SampleCount ?? 0;
    public IReadOnlyList<EdpDistributionSpec> Specs => _specs;

    /// <summary>
    /// The Pearson correlation matrix set via <see cref="SetCorrelation"/>, or
    /// <see langword="null"/> if none was set (or if a copula was used instead).
    /// </summary>
    public double[,]? CorrelationMatrix => _correlationMatrix;

    /// <summary>
    /// The explicit copula set via <see cref="SetCopula"/>, or
    /// <see langword="null"/> if none was set (or if a matrix was used instead).
    /// </summary>
    public ICopula? ActiveCopula => _copula;

    // ─── Private: MLE fitting ─────────────────────────────────────────────────

    private static (double theta1, double theta2) FitMarginal(double[] data, EdpDistributionKind kind)
    {
        if (kind == EdpDistributionKind.Lognormal)
        {
            // MLE for lognormal = sample mean and std-dev of ln(x).
            double[] lnData = [.. data
                .Where(x => x > 0)
                .Select(x => Math.Log(x))];
            if (lnData.Length == 0)
                throw new InvalidOperationException("All EDP values are non-positive; cannot fit lognormal.");
            double mu = lnData.Average();
            double variance = lnData.Average(x => (x - mu) * (x - mu));
            return (mu, Math.Sqrt(variance));
        }

        // Normal: MLE = sample mean, biased std-dev (= MLE estimator).
        var dist = new Normal();
        dist.Estimate(data, ParameterEstimationMethod.MaximumLikelihood);
        return (dist.Mu, dist.Sigma);
    }

    private void EnsureSampled()
    {
        if (_registry is null)
            throw new InvalidOperationException("Call GenerateSample() first.");
    }
}
