// ReSilico – Probabilistic Damage and Loss Assessment Engine
// RandomVariableRegistry: central registry with injected ISampler

using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;

namespace ReSilico.Core.RandomVariables;

/// <summary>
/// Central manager for all <see cref="RandomVariable"/> and
/// <see cref="RandomVariableSet"/> instances in an assessment.
///
/// Pipeline (called by <see cref="GenerateSample"/>):
///   1. Generate independent uniform [0,1] samples for every variable via the
///      injected <see cref="ISampler"/> (MC or LHS).
///   2. For each <see cref="RandomVariableSet"/>, apply the Gaussian copula to
///      transform the relevant columns to correlated uniforms.
///   3. Apply each variable's (truncated) inverse CDF to produce physical samples.
///
/// Injecting an <see cref="ISampler"/> (constructor parameter) enables
/// easy substitution of sampling strategies without changing domain code.
/// </summary>
/// <param name="sampler">
/// Uniform sampling strategy.  Defaults to <see cref="LatinHypercubeSampler.Standard"/>
/// when not supplied — LHS provides better variance reduction than pure MC.
/// </param>
public sealed class RandomVariableRegistry(ISampler? sampler = null)
{
    private readonly ISampler _sampler = sampler ?? LatinHypercubeSampler.Standard;
    private readonly Dictionary<string, RandomVariable> _variables = [];
    private readonly Dictionary<string, RandomVariableSet> _sets = [];
    private Dictionary<string, int>? _colIndex;   // variable name → column
    private double[,]? _samples;                   // [count, nVars]

    // ─── Registration ─────────────────────────────────────────────────────────

    public void Register(RandomVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        if (!_variables.TryAdd(variable.Name, variable))
            throw new InvalidOperationException($"Variable '{variable.Name}' is already registered.");
        InvalidateIndex();
    }

    public void Register(IEnumerable<RandomVariable> variables)
    {
        foreach (var v in variables) Register(v);
    }

    public void RegisterSet(RandomVariableSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _sets[set.Name] = set;
        foreach (var v in set.Variables)
            if (!_variables.ContainsKey(v.Name))
                Register(v);
    }

    public bool Contains(string name) => _variables.ContainsKey(name);
    public RandomVariable Get(string name) => _variables[name];

    // ─── Sample generation ────────────────────────────────────────────────────

    /// <summary>
    /// Generate <paramref name="count"/> realisations for every registered variable.
    /// </summary>
    public void GenerateSample(int count, int seed = -1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0, nameof(count));
        BuildColumnIndex();

        int n = _variables.Count;

        // Step 1: independent uniform samples for all variables.
        double[,] uniform = _sampler.GenerateUniform(count, n, seed);

        // Step 2: apply Gaussian copula within each registered set.
        foreach (var set in _sets.Values)
        {
            int setN = set.Count;
            int[] cols = [.. set.Variables.Select(v => _colIndex![v.Name])];

            var subUniform = new double[count, setN];
            for (int i = 0; i < count; i++)
                for (int j = 0; j < setN; j++)
                    subUniform[i, j] = uniform[i, cols[j]];

            set.ApplyCorrelation(subUniform);

            for (int i = 0; i < count; i++)
                for (int j = 0; j < setN; j++)
                    uniform[i, cols[j]] = subUniform[i, j];
        }

        // Step 3: inverse CDF per variable — use vectorized BatchInverseTransform.
        _samples = new double[count, n];
        foreach (var (name, rv) in _variables)
        {
            int col = _colIndex![name];
            // Extract the column into a temporary span-friendly array.
            var uCol = new double[count];
            for (int i = 0; i < count; i++) uCol[i] = uniform[i, col];

            double[] xCol = rv.BatchInverseTransform(uCol);
            for (int i = 0; i < count; i++) _samples[i, col] = xCol[i];
        }
    }

    // ─── Access ───────────────────────────────────────────────────────────────

    /// <summary>Return all realisations of variable <paramref name="name"/> as a column vector.</summary>
    public double[] GetSample(string name)
    {
        EnsureSampled();
        int col = _colIndex![name];
        int n = _samples!.GetLength(0);
        var result = new double[n];
        for (int i = 0; i < n; i++) result[i] = _samples[i, col];
        return result;
    }

    /// <summary>Full [count × nVars] sample matrix.</summary>
    public double[,] GetAllSamples()
    {
        EnsureSampled();
        return _samples!;
    }

    public IReadOnlyList<string> GetVariableNames()
    {
        BuildColumnIndex();
        return [.. _colIndex!.Keys];
    }

    public int SampleCount => _samples?.GetLength(0) ?? 0;

    // ─── Private ──────────────────────────────────────────────────────────────

    private void BuildColumnIndex()
    {
        if (_colIndex is not null) return;
        _colIndex = [];
        int col = 0;
        foreach (var name in _variables.Keys)
            _colIndex[name] = col++;
    }

    private void InvalidateIndex()
    {
        _colIndex = null;
        _samples = null;
    }

    private void EnsureSampled()
    {
        if (_samples is null)
            throw new InvalidOperationException("Call GenerateSample() before accessing samples.");
    }
}
