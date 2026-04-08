// ReSilico – Probabilistic Damage and Loss Assessment Engine
// ConsequenceFunction: DS → loss consequence (cost, time, casualties)

using ReSilico.Core.Distributions;
using ReSilico.Domain.Enums;

namespace ReSilico.Analysis.Loss;

/// <summary>
/// Defines the loss consequence associated with a particular damage state for
/// a specific decision variable.
///
/// Loss quantity is modelled as a lognormal random variable:
///   L | DS = ds  ~  Lognormal(θ = median_loss, β = dispersion)
///
/// A deterministic (zero-dispersion) mode is supported when <see cref="Beta"/> = 0,
/// in which case the median is returned directly.
/// </summary>
public sealed class ConsequenceFunction
{
    private readonly LognormalDistribution? _dist;

    /// <param name="damageStateIndex">Index of the damage state this consequence applies to.</param>
    /// <param name="decisionVariable">Which loss metric this models.</param>
    /// <param name="medianLoss">Median loss value for this DS (in the DV's units).</param>
    /// <param name="beta">Dispersion β. Use 0 for a deterministic consequence.</param>
    public ConsequenceFunction(
        int damageStateIndex,
        DecisionVariable decisionVariable,
        double medianLoss,
        double beta = 0.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(damageStateIndex, nameof(damageStateIndex));
        ArgumentOutOfRangeException.ThrowIfNegative(medianLoss, nameof(medianLoss));
        ArgumentOutOfRangeException.ThrowIfNegative(beta, nameof(beta));

        DamageStateIndex = damageStateIndex;
        DecisionVariable = decisionVariable;
        MedianLoss = medianLoss;
        Beta = beta;

        if (beta > 0.0 && medianLoss > 0.0)
            _dist = LognormalDistribution.FromMedianAndDispersion(medianLoss, beta);
    }

    public int DamageStateIndex { get; }
    public DecisionVariable DecisionVariable { get; }
    public double MedianLoss { get; }
    public double Beta { get; }

    /// <summary>True if the consequence is deterministic (β = 0).</summary>
    public bool IsDeterministic => Beta <= 0.0 || _dist is null;

    /// <summary>
    /// Sample a realised loss value given a uniform U(0,1) variate.
    /// If deterministic, returns <see cref="MedianLoss"/> regardless of <paramref name="u"/>.
    /// </summary>
    public double SampleLoss(double u)
    {
        if (MedianLoss <= 0.0) return 0.0;
        if (IsDeterministic) return MedianLoss;
        return _dist!.InverseCDF(Math.Clamp(u, 1e-14, 1.0 - 1e-14));
    }

    /// <summary>
    /// Return the median loss value (50th percentile).
    /// Equivalent to calling SampleLoss(0.5).
    /// </summary>
    public double MedianValue => MedianLoss;

    public override string ToString() =>
        $"DS{DamageStateIndex} → {DecisionVariable}: median={MedianLoss:G4}, β={Beta:G4}";
}
