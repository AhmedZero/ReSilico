// ReSilico – Probabilistic Damage and Loss Assessment Engine
// AdaptiveSimulationResult: extended result from adaptive Monte Carlo

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Loss;

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Result of an adaptive Monte Carlo run.
///
/// Extends <see cref="SimulationResult"/> with convergence metadata and a
/// full per-iteration diagnostic history.  All base-class statistics
/// (mean cost, CVaR, percentiles, histograms, etc.) operate on the complete
/// merged sample across all adaptive iterations.
///
/// Usage:
/// <code>
/// AdaptiveSimulationResult r = pipeline.RunAdaptive(options);
///
/// Console.WriteLine($"Converged: {r.Converged}  after {r.Iterations} iterations");
/// Console.WriteLine($"Total samples used: {r.NumberOfSimulations}");
/// Console.WriteLine($"Final CVaR(95%): {r.FinalCVaR:N0}");
///
/// foreach (var d in r.Diagnostics)
///     Console.WriteLine($"  [{d.Iteration}] N={d.TotalSamples}  " +
///         $"mean={d.MeanCost:N0}  CVaR={d.CVaR:N0}  " +
///         $"mean_err={d.MeanRelativeError:P2}  tail_chg={d.TailRelativeChange:P2}");
/// </code>
/// </summary>
public sealed class AdaptiveSimulationResult : SimulationResult
{
    internal AdaptiveSimulationResult(
        LossSample   loss,
        DamageSample damage,
        int          totalSamples,
        bool         converged,
        int          iterations,
        IReadOnlyList<ConvergenceDiagnostic> diagnostics)
        : base(loss, damage, totalSamples)
    {
        Converged   = converged;
        Iterations  = iterations;
        Diagnostics = diagnostics;
    }

    // ── Convergence metadata ──────────────────────────────────────────────────

    /// <summary>
    /// True if both convergence criteria were satisfied before
    /// <see cref="AdaptiveOptions.MaxSamples"/> was reached.
    /// False means the run hit the sample cap before converging.
    /// </summary>
    public bool Converged { get; }

    /// <summary>Number of batches executed (initial + subsequent).</summary>
    public int Iterations { get; }

    /// <summary>
    /// Per-iteration convergence diagnostics, one entry per completed batch.
    /// Ordered by iteration number (first entry = initial batch).
    /// </summary>
    public IReadOnlyList<ConvergenceDiagnostic> Diagnostics { get; }

    // ── Convenience accessors ─────────────────────────────────────────────────

    /// <summary>
    /// CVaR at the target quantile from the final (converged) iteration.
    /// Returns <see cref="double.NaN"/> if no iterations were recorded.
    /// </summary>
    public double FinalCVaR =>
        Diagnostics.Count > 0 ? Diagnostics[^1].CVaR : double.NaN;

    /// <summary>
    /// Relative standard error of the mean from the final iteration.
    /// Returns <see cref="double.NaN"/> if no iterations were recorded.
    /// </summary>
    public double FinalMeanRelativeError =>
        Diagnostics.Count > 0 ? Diagnostics[^1].MeanRelativeError : double.NaN;

    /// <summary>
    /// Print the convergence history to the console.
    /// </summary>
    public void PrintConvergenceHistory()
    {
        string line = new('─', 90);
        Console.WriteLine(line);
        Console.WriteLine(" Adaptive Monte Carlo – Convergence History");
        Console.WriteLine(line);
        Console.WriteLine(
            $"  {"Iter",4}  {"Samples",9}  {"Mean Cost",14}  {"CVaR(p)",14}  " +
            $"{"Mean RelErr",12}  {"Tail ΔRelErr",12}  {"Converged?",10}");
        Console.WriteLine(line);

        foreach (var d in Diagnostics)
        {
            bool iterConverged =
                d.MeanRelativeError < double.PositiveInfinity &&
                d.TailRelativeChange < double.PositiveInfinity &&
                Converged && d == Diagnostics[^1];

            Console.WriteLine(
                $"  {d.Iteration,4}  {d.TotalSamples,9:N0}  {d.MeanCost,14:N0}  " +
                $"{d.CVaR,14:N0}  {d.MeanRelativeError,12:P2}  " +
                $"{d.TailRelativeChange,12:P2}  {(iterConverged ? "✓" : ""),10}");
        }

        Console.WriteLine(line);
        Console.WriteLine($"  Status  : {(Converged ? "CONVERGED" : "MAX SAMPLES REACHED")}");
        Console.WriteLine($"  Samples : {NumberOfSimulations:N0}  ({Iterations} iterations)");
        Console.WriteLine(line);
    }
}
