// ReSilico.IO – Assessment-level IO
//
// Top-level save/load API for a complete ReSilico assessment.
//
// Directory layout written by SaveResults():
//
//   <dir>/
//     demand_marginals.csv
//     demand_correlation.csv
//     demand_sample.csv
//     damage_sample.csv
//     loss_sample.csv
//
// LoadResults() reads whatever is present and returns a populated
// AssessmentSnapshot with nullable members for absent files.

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using System;
using System.IO;

namespace ReSilico.IO;

/// <summary>
/// Snapshot of a complete assessment that can be serialised / deserialised.
/// All members are optional — absent files are represented as null.
/// </summary>
public sealed class AssessmentSnapshot
{
    public DemandModel?  DemandModel   { get; init; }
    public DamageSample? DamageSample  { get; init; }
    public LossSample?   LossSample    { get; init; }
    public SimulationResult? Result    { get; init; }
}

/// <summary>
/// Saves and loads a complete ReSilico assessment using the pelicun CSV conventions.
/// </summary>
public static class AssessmentIO
{
    private const string DemandPrefix  = "demand";
    private const string DamagePrefix  = "damage";
    private const string LossPrefix    = "loss";

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Save a <see cref="SimulationResult"/> (and optionally the demand model
    /// and fragility/loss databases) into <paramref name="outputDirectory"/>.
    ///
    /// <para>Creates the directory if it does not exist.</para>
    /// </summary>
    public static void SaveResults(
        string outputDirectory,
        SimulationResult result,
        DemandModel?     demandModel    = null,
        double[,]?       correlation    = null)
    {
        Directory.CreateDirectory(outputDirectory);

        // ── Demand model ─────────────────────────────────────────────────────
        if (demandModel is not null)
        {
            string demandPfx = Path.Combine(outputDirectory, DemandPrefix);
            DemandModelIO.SaveModel(demandPfx, demandModel, correlation);

            if (demandModel.SampleCount > 0)
                DemandModelIO.SaveSample(demandPfx, demandModel);
        }

        // ── Damage sample ─────────────────────────────────────────────────────
        var damageSample = result.DamageSample;
        if (damageSample.Count > 0)
            DamageModelIO.SaveDamageSample(
                Path.Combine(outputDirectory, DamagePrefix), damageSample);

        // ── Loss sample ───────────────────────────────────────────────────────
        var lossSample = result.LossSample;
        if (lossSample.Count > 0)
            LossModelIO.SaveLossSample(
                Path.Combine(outputDirectory, LossPrefix), lossSample);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Load whatever assessment files exist in <paramref name="inputDirectory"/>
    /// and return an <see cref="AssessmentSnapshot"/>.
    /// </summary>
    public static AssessmentSnapshot LoadResults(string inputDirectory)
    {
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException($"Directory not found: {inputDirectory}");

        DemandModel?  demand  = TryLoad(() => DemandModelIO.LoadModel(Path.Combine(inputDirectory, DemandPrefix)));
        DamageSample? damage  = TryLoad(() => DamageModelIO.LoadDamageSample(Path.Combine(inputDirectory, DamagePrefix)));
        LossSample?   loss    = TryLoad(() => LossModelIO.LoadLossSample(Path.Combine(inputDirectory, LossPrefix)));

        return new AssessmentSnapshot
        {
            DemandModel  = demand,
            DamageSample = damage,
            LossSample   = loss
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T? TryLoad<T>(Func<T> loader) where T : class
    {
        try   { return loader(); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>
    /// Convenience: save a <see cref="SimulationResult"/> to a time-stamped
    /// sub-directory under <paramref name="baseDirectory"/>, e.g.:
    ///   <c>results/2025-06-01_14-30-00/</c>
    /// Returns the full path of the created directory.
    /// </summary>
    public static string SaveResultsTimestamped(
        string baseDirectory,
        SimulationResult result,
        DemandModel?   demandModel  = null,
        double[,]?     correlation  = null)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string dir       = Path.Combine(baseDirectory, timestamp);
        SaveResults(dir, result, demandModel, correlation);
        return dir;
    }
}
