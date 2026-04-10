// ReSilico.IO – Demand Model IO
//
// Files produced:
//   <prefix>_marginals.csv   – EDP distribution parameters (row-keyed by EDP key)
//   <prefix>_correlation.csv – Pearson correlation matrix
//   <prefix>_sample.csv      – Realised EDP sample (column-keyed by EDP key)

using ReSilico.Analysis.Demand;
using ReSilico.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ReSilico.IO;

public static class DemandModelIO
{
    // ── Marginals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Save demand marginal parameters to <c>&lt;prefix&gt;_marginals.csv</c>.
    /// </summary>
    /// <param name="filePrefix">Full path prefix, e.g. "output/demand".</param>
    public static void SaveMarginals(string filePrefix, IReadOnlyList<EdpDistributionSpec> specs)
    {
        string path = filePrefix + "_marginals.csv";

        string[] headers = ["Units", "Family", "Theta_0", "Theta_1", "TruncateLower", "TruncateUpper"];
        string[] unitRow = ["", "", "", "", "", ""];

        var rows = specs.Select(s =>
        {
            string unit   = Units.ForEdpType(s.Edp.Type);
            string family = s.Kind == EdpDistributionKind.Lognormal ? "lognormal" : "normal";
            return (
                key: s.Edp.Key,
                values: new[]
                {
                    unit,
                    family,
                    ReSilicoCsv.FormatDouble(s.Theta1),
                    ReSilicoCsv.FormatDouble(s.Theta2),
                    double.IsNaN(s.TruncLower) ? string.Empty : ReSilicoCsv.FormatDouble(s.TruncLower),
                    double.IsNaN(s.TruncUpper) ? string.Empty : ReSilicoCsv.FormatDouble(s.TruncUpper)
                }
            );
        });

        var table = ReSilicoCsv.Build(headers, unitRow, rows);
        ReSilicoCsv.Write(path, table);
    }

    /// <summary>
    /// Load demand marginal parameters from <c>&lt;prefix&gt;_marginals.csv</c>.
    /// Returns specs that can be fed directly to <see cref="DemandModel.AddEdp"/>.
    /// </summary>
    public static List<EdpDistributionSpec> LoadMarginals(string filePrefix)
    {
        string path = filePrefix + "_marginals.csv";
        if (!File.Exists(path))
            throw new FileNotFoundException($"Marginals file not found: {path}");

        var table = ReSilicoCsv.Read(path);
        var specs = new List<EdpDistributionSpec>();

        foreach (var key in table.RowKeys)
        {
            var parts = ReSilicoCsv.SplitKey(key); // e.g. PID-1-1
            if (parts.Length < 1) continue;

            string type  = parts[0];
            int location = parts.Length > 1 && int.TryParse(parts[1], out var loc) ? loc : 1;
            int dir      = parts.Length > 2 && int.TryParse(parts[2], out var d)   ? d   : 1;

            var edp    = new EDP(type, location, dir);
            var family = table.Get(key, "Family");
            var kind   = family.Equals("lognormal", StringComparison.OrdinalIgnoreCase)
                       ? EdpDistributionKind.Lognormal
                       : EdpDistributionKind.Normal;

            double theta1  = table.GetDouble(key, "Theta_0");
            double theta2  = table.GetDouble(key, "Theta_1");
            double truncLo = table.GetDouble(key, "TruncateLower", double.NaN);
            double truncHi = table.GetDouble(key, "TruncateUpper", double.NaN);

            specs.Add(new EdpDistributionSpec(edp, kind, theta1, theta2, truncLo, truncHi));
        }

        return specs;
    }

    // ── Correlation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Save the Pearson correlation matrix to <c>&lt;prefix&gt;_correlation.csv</c>.
    /// </summary>
    public static void SaveCorrelation(string filePrefix, double[,] matrix, IReadOnlyList<EDP> edps)
    {
        int n = edps.Count;
        if (matrix.GetLength(0) != n || matrix.GetLength(1) != n)
            throw new ArgumentException("Matrix dimensions must match number of EDPs.");

        string path  = filePrefix + "_correlation.csv";
        var keys     = edps.Select(e => e.Key).ToArray();
        var units    = EmptyStrings(n);

        var rows = keys.Select((rowKey, r) => (
            key: rowKey,
            values: Enumerable.Range(0, n)
                .Select(c => ReSilicoCsv.FormatDouble(matrix[r, c]))
                .ToArray()
        ));

        var table = ReSilicoCsv.Build(keys, units, rows);
        ReSilicoCsv.Write(path, table);
    }

    /// <summary>
    /// Load the correlation matrix from <c>&lt;prefix&gt;_correlation.csv</c>.
    /// Returns the matrix and the ordered EDP keys.
    /// </summary>
    public static (double[,] matrix, string[] edpKeys) LoadCorrelation(string filePrefix)
    {
        string path = filePrefix + "_correlation.csv";
        if (!File.Exists(path)) return (new double[0, 0], []);

        var table  = ReSilicoCsv.Read(path);
        int n      = table.RowKeys.Count;
        var keys   = table.RowKeys.ToArray();
        var matrix = new double[n, n];

        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                matrix[r, c] = table.GetDouble(keys[r], keys[c], r == c ? 1.0 : 0.0);

        return (matrix, keys);
    }

    // ── Sample ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Save EDP sample to <c>&lt;prefix&gt;_sample.csv</c>.
    /// Columns: one per EDP key; rows: realisations 0…N-1.
    /// </summary>
    public static void SaveSample(string filePrefix, DemandModel model)
    {
        int n = model.SampleCount;
        if (n == 0) throw new InvalidOperationException("No sample generated yet.");

        var specs   = model.Specs;
        var headers = specs.Select(s => s.Edp.Key).ToArray();
        var units   = specs.Select(s => Units.ForEdpType(s.Edp.Type)).ToArray();

        var rows = Enumerable.Range(0, n).Select(i =>
        {
            var values = specs
                .Select(s => ReSilicoCsv.FormatDouble(model.GetEdpSample(s.Edp)[i]))
                .ToArray();
            return (key: i.ToString(CultureInfo.InvariantCulture), values);
        });

        var table = ReSilicoCsv.Build(headers, units, rows);
        ReSilicoCsv.Write(filePrefix + "_sample.csv", table);
    }

    /// <summary>
    /// Load an EDP sample CSV and fit marginals to the empirical data via
    /// <see cref="DemandModel.CalibrateFromData"/>.
    /// </summary>
    public static DemandModel LoadSampleAndFit(
        string filePrefix,
        EdpDistributionKind kind = EdpDistributionKind.Lognormal,
        double[,]? correlationMatrix = null)
    {
        string path = filePrefix + "_sample.csv";
        if (!File.Exists(path))
            throw new FileNotFoundException($"Sample file not found: {path}");

        var table  = ReSilicoCsv.Read(path);
        int nEdps  = table.Headers.Length;
        int nRows  = table.RowKeys.Count;

        // Reconstruct EDP objects from column headers ("PID-1-1" → EDP("PID",1,1))
        var edps = table.Headers.Select(h =>
        {
            var parts = ReSilicoCsv.SplitKey(h);
            string type = parts[0];
            int loc = parts.Length > 1 && int.TryParse(parts[1], out var l) ? l : 1;
            int dir = parts.Length > 2 && int.TryParse(parts[2], out var d) ? d : 1;
            return new EDP(type, loc, dir);
        }).ToList();

        var rawSamples = new double[nRows, nEdps];
        for (int r = 0; r < nRows; r++)
        {
            string rowKey = table.RowKeys[r];
            for (int c = 0; c < nEdps; c++)
            {
                string cell = table.Rows[rowKey][c];
                rawSamples[r, c] = double.TryParse(cell, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var v) ? v : 0.0;
            }
        }

        var model = new DemandModel();
        model.CalibrateFromData(edps, rawSamples, kind);

        if (correlationMatrix is not null)
            model.SetCorrelation(correlationMatrix);

        return model;
    }

    // ── Convenience wrappers ──────────────────────────────────────────────────

    /// <summary>
    /// Save marginals + optional correlation matrix using a single prefix.
    /// </summary>
    public static void SaveModel(string filePrefix, DemandModel model, double[,]? correlation = null)
    {
        SaveMarginals(filePrefix, model.Specs);
        if (correlation is not null && model.Specs.Count > 0)
            SaveCorrelation(filePrefix, correlation, model.Specs.Select(s => s.Edp).ToList());
    }

    /// <summary>
    /// Load marginals + optional correlation and return a ready-to-use <see cref="DemandModel"/>.
    /// </summary>
    public static DemandModel LoadModel(string filePrefix)
    {
        var specs = LoadMarginals(filePrefix);
        var model = new DemandModel();
        foreach (var spec in specs) model.AddEdp(spec);

        var (matrix, _) = LoadCorrelation(filePrefix);
        if (matrix.GetLength(0) == specs.Count && specs.Count > 0)
            model.SetCorrelation(matrix);

        return model;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] EmptyStrings(int count)
    {
        var arr = new string[count];
        Array.Fill(arr, string.Empty);
        return arr;
    }
}
