// ReSilico – Probabilistic Damage and Loss Assessment Engine
// ICopula: abstraction for copula transformations

namespace ReSilico.Core.Copulas;

/// <summary>
/// Transforms a matrix of independent U(0,1) samples into correlated uniform
/// samples in-place using a specific dependence structure.
///
/// The input and output are both [count × dimension] matrices of values ∈ (0,1).
/// Each row is one realisation; each column is one marginal variable.
///
/// Usage in the sampling pipeline (RandomVariableRegistry):
///   1. Draw independent U(0,1) for every variable (MC or LHS).
///   2. Call <see cref="ApplyCorrelation"/> to impose the copula structure.
///   3. Apply each variable's inverse-CDF to the now-correlated uniforms.
/// </summary>
public interface ICopula
{
    /// <summary>
    /// Transform <paramref name="uniformSamples"/> (shape [count, dimension])
    /// from independent to copula-correlated uniform values, in-place.
    /// </summary>
    void ApplyCorrelation(double[,] uniformSamples);

    /// <summary>
    /// Dimension of the copula (number of variables in the set).
    /// </summary>
    int Dimension { get; }
}
