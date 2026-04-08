// ReSilico – Probabilistic Damage and Loss Assessment Engine
// DamageModel: EDP → damage state assignments with ArrayPool + Parallel.For

using System.Buffers;
using ReSilico.Core.Distributions;
using ReSilico.Core.Sampling;
using ReSilico.Domain;

namespace ReSilico.Analysis.Damage;

/// <summary>
/// Per-simulation damage state result matrix.
/// </summary>
public sealed class DamageSample(IReadOnlyList<string> componentIds, int count)
{
    private readonly List<string> _ids = [.. componentIds];

    public IReadOnlyList<string> ComponentIds => _ids;
    public int[,] DamageStates { get; } = new int[count, componentIds.Count];
    public int Count => DamageStates.GetLength(0);
    public int NumberOfComponents => DamageStates.GetLength(1);

    /// <summary>
    /// Empirical probability mass across damage states for one component.
    /// Returns an array indexed [0..maxDS].
    /// </summary>
    public double[] GetDamageStateProbabilities(string componentId, int maxDamageState)
    {
        int col = _ids.IndexOf(componentId);
        if (col < 0) throw new ArgumentException($"Component '{componentId}' not found.");

        var counts = new int[maxDamageState + 1];
        int n = Count;
        for (int i = 0; i < n; i++)
            counts[Math.Min(DamageStates[i, col], maxDamageState)]++;

        var probs = new double[maxDamageState + 1];
        for (int k = 0; k <= maxDamageState; k++)
            probs[k] = counts[k] / (double)n;
        return probs;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Converts EDP samples into component damage state assignments using fragility
/// functions and Monte Carlo capacity sampling.
///
/// Algorithm (per realisation, per component):
///   1. Retrieve the realised EDP for the governing demand type.
///   2. For each limit state k (ascending):
///      • A fresh U(0,1) capacity sample uₖ is drawn.
///      • LS k is exceeded iff uₖ &lt; P(DS ≥ k | EDP).
///      • Stop at the first un-exceeded LS (progressive damage).
///   3. The highest exceeded LS index becomes the realised damage state.
///
/// Performance:
///   • Capacity sample matrix pre-generated once (MC; seeded).
///   • Parallel.For with thread-local <see cref="ArrayPool{T}"/> buffers
///     eliminates heap allocations inside the hot loop.
///   • Fragility batch evaluation uses SIMD z-score vectorization.
/// </summary>
public sealed class DamageModel
{
    private readonly List<ComponentFragilitySpec> _specs = [];

    // ─── Registration ─────────────────────────────────────────────────────────

    public void Add(ComponentFragilitySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _specs.Add(spec);
    }

    public void Add(IEnumerable<ComponentFragilitySpec> specs)
    {
        foreach (var s in specs) Add(s);
    }

    public IReadOnlyList<ComponentFragilitySpec> Specs => _specs;

    // ─── Assessment ───────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate damage states for all registered components across
    /// <paramref name="count"/> Monte Carlo realisations.
    ///
    /// <paramref name="edpSamples"/> — a function returning the realised EDP
    /// array (length = count) for a given EDP key string.
    /// </summary>
    public DamageSample Evaluate(
        Func<string, double[]> edpSamples,
        int count,
        int seed = -1)
    {
        ArgumentNullException.ThrowIfNull(edpSamples);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0, nameof(count));

        if (_specs.Count == 0)
            throw new InvalidOperationException("No component fragility specifications registered.");

        int nComp = _specs.Count;
        var componentIds = _specs.Select(s => s.Component.Id).ToList();
        var result = new DamageSample(componentIds, count);

        // Pre-fetch EDP arrays (avoids repeated delegate invocations in the loop).
        var edpArrays = new double[nComp][];
        var maxLS = new int[nComp];
        for (int c = 0; c < nComp; c++)
        {
            edpArrays[c] = edpSamples(_specs[c].Component.GoverningEdp.Key);
            maxLS[c] = _specs[c].NumberOfLimitStates;
        }

        // Pre-generate all capacity U(0,1) samples in one batch.
        int totalLS = maxLS.Sum();
        var capSampler = new MonteCarloSampler();
        double[,] capUniforms = capSampler.GenerateUniform(count, totalLS, seed);

        // Column offset for each component within capUniforms.
        int[] lsOffsets = new int[nComp];
        int off = 0;
        for (int c = 0; c < nComp; c++) { lsOffsets[c] = off; off += maxLS[c]; }

        int maxLSAny = maxLS.Length > 0 ? maxLS.Max() : 0;

        // Parallel evaluation — thread-local buffer for capacity samples per component.
        Parallel.For(0, count,
            () => new double[maxLSAny],   // thread-local buffer
            (i, _, capsBuf) =>
            {
                for (int c = 0; c < nComp; c++)
                {
                    double edp = edpArrays[c][i];
                    int nLS = maxLS[c];
                    int lsOff = lsOffsets[c];

                    for (int k = 0; k < nLS; k++)
                        capsBuf[k] = capUniforms[i, lsOff + k];

                    result.DamageStates[i, c] =
                        _specs[c].EvaluateDamageState(edp, capsBuf.AsSpan(0, nLS));
                }
                return capsBuf;
            },
            _ => { });   // no finalizer needed

        return result;
    }
}
