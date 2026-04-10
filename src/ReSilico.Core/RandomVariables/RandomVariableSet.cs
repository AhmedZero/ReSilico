// ReSilico – Probabilistic Damage and Loss Assessment Engine
// RandomVariableSet: correlated group of RVs via a pluggable ICopula

using ReSilico.Core.Copulas;
using ReSilico.Core.Distributions;

namespace ReSilico.Core.RandomVariables;

/// <summary>
/// A named group of <see cref="RandomVariable"/> instances connected by a
/// pluggable copula (default: Gaussian copula).
///
/// The copula transforms a [count × n] matrix of independent U(0,1) samples
/// into correlated uniforms; the <see cref="ApplyCorrelation"/> method delegates
/// entirely to <see cref="Copula"/>.
///
/// Swapping dependence structures (e.g., Gaussian → t-Copula) requires only:
/// <code>
///   set.Copula = new TCopula(df: 5, correlationMatrix: rho);
/// </code>
///
/// Backward compatibility:
///   The constructor that accepts a raw <c>double[,]</c> correlation matrix
///   creates a <see cref="GaussianCopula"/> automatically — all existing code
///   continues to work without modification.
///
/// Thread safety: <see cref="ApplyCorrelation"/> is thread-safe once the copula
/// itself is thread-safe (both built-in copulas satisfy this).
/// </summary>
public sealed class RandomVariableSet
{
    private readonly RandomVariable[] _variables;

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>
    /// Create a correlated set using a <see cref="GaussianCopula"/> built from
    /// <paramref name="correlationMatrix"/>.
    /// This constructor is fully backward-compatible with the previous API.
    /// </summary>
    public RandomVariableSet(
        string name,
        IReadOnlyList<RandomVariable> variables,
        double[,] correlationMatrix)
        : this(name, variables, new GaussianCopula(correlationMatrix))
    {
    }

    /// <summary>
    /// Create a correlated set with a specific <paramref name="copula"/>.
    /// Use this constructor to plug in a t-Copula or any custom copula.
    /// </summary>
    public RandomVariableSet(
        string name,
        IReadOnlyList<RandomVariable> variables,
        ICopula copula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(copula);
        if (copula.Dimension != variables.Count)
            throw new ArgumentException("Copula dimension mismatch.");
        Name      = name;
        _variables = [.. variables];
        Copula    = copula;
    }

    // ─── Properties ──────────────────────────────────────────────────────────

    public string Name { get; }
    public int Count => _variables.Length;
    public IReadOnlyList<RandomVariable> Variables => _variables;

    /// <summary>
    /// The copula that governs the dependence structure.
    /// Defaults to <see cref="GaussianCopula"/> when constructed with a
    /// raw correlation matrix.
    ///
    /// Assign a new copula at any time (before the next <see cref="ApplyCorrelation"/>
    /// call) to switch the dependence model:
    /// <code>
    ///   set.Copula = new TCopula(df: 4, correlationMatrix: rho);
    /// </code>
    /// </summary>
    public ICopula Copula
    {
        get => field;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }
    // ─── ApplyCorrelation ─────────────────────────────────────────────────────

    /// <summary>
    /// Transform the [count × n] matrix of independent U(0,1) samples into
    /// correlated uniforms according to <see cref="Copula"/>, in-place.
    /// </summary>
    public void ApplyCorrelation(double[,] uniformSamples)
    {
        if (uniformSamples.GetLength(1) != Count)
            throw new ArgumentException("Sample dimension mismatch.");
        Copula.ApplyCorrelation(uniformSamples);
    }
}
