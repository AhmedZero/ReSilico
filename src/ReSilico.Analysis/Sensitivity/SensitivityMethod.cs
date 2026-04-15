// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SensitivityMethod: enumeration of supported sensitivity analysis strategies

namespace ReSilico.Analysis.Sensitivity;

/// <summary>
/// Specifies the algorithmic strategy used by <see cref="SensitivityAnalyzer"/>.
///
/// <list type="bullet">
///   <item>
///     <term><see cref="Local"/></term>
///     <description>
///       Centered finite-difference approximation of ∂f/∂θ.  Two extra
///       simulations per parameter (θ± = θ · (1 ± ε)).  Fast and exact for
///       smooth, monotone response surfaces.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Global"/></term>
///     <description>
///       Simplified Morris elementary-effects screening.  Each parameter is
///       swept over <see cref="SensitivityOptions.GlobalLevels"/> evenly-spaced
///       levels spanning [θ(1−2ε), θ(1+2ε)].  Reports the mean absolute
///       elementary effect (Morris μ*) and a variance-ratio proxy for the
///       first-order Sobol index.  Captures non-linear and threshold effects
///       missed by the local method.
///     </description>
///   </item>
///   <item>
///     <term><see cref="Correlation"/></term>
///     <description>
///       Perturbs off-diagonal entries of the EDP correlation matrix one pair
///       at a time and measures the change in the selected tail metric (CVaR
///       or percentile).  Requires a Gaussian or t-Copula to be configured on
///       the demand model.
///     </description>
///   </item>
/// </list>
/// </summary>
public enum SensitivityMethod
{
    Local,
    Global,
    Correlation,
}
