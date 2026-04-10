// ReSilico.IO – Damage Model IO
//
// Fragility database schema (FEMA P-58 / HAZUS compatible):
//
//   Index: component ID (e.g. "B1033.001a")
//   Cols:  Demand-Type, Demand-Unit, Demand-Offset, Demand-Directional,
//          LS1-Family, LS1-Theta_0, LS1-Theta_1, [LS2-..., LS3-...]
//
// Damage sample schema (<prefix>_sample.csv):
//
//   Header row: component IDs
//   Units row:  "ea" for each component
//   Data rows:  realisation index → integer damage state per component

using ReSilico.Analysis.Damage;
using ReSilico.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ReSilico.IO;

public static class DamageModelIO
{
    // ── Fragility database ────────────────────────────────────────────────────

    /// <summary>
    /// Load a fragility database CSV and build <see cref="ComponentFragilitySpec"/>
    /// objects for each component found in <paramref name="asset"/>.
    ///
    /// The database may contain more components than the asset — only matching IDs
    /// are returned.
    /// </summary>
    public static List<ComponentFragilitySpec> LoadFragilityDatabase(
        string csvPath,
        Asset asset)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"Fragility database not found: {csvPath}");

        var table    = ReSilicoCsv.Read(csvPath);
        var result   = new List<ComponentFragilitySpec>();
        var compById = asset.Components.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var compId in table.RowKeys)
        {
            if (!compById.TryGetValue(compId, out var comp)) continue;

            var spec = new ComponentFragilitySpec(comp);

            for (int ls = 1; ; ls++)
            {
                string familyCol = $"LS{ls}-Family";
                string theta0Col = $"LS{ls}-Theta_0";
                string theta1Col = $"LS{ls}-Theta_1";

                if (!table.HasColumn(familyCol)) break;

                var family = table.Get(compId, familyCol);
                if (string.IsNullOrWhiteSpace(family)) break;

                double median = table.GetDouble(compId, theta0Col);
                double beta   = table.GetDouble(compId, theta1Col);
                if (double.IsNaN(median) || double.IsNaN(beta)) break;

                spec.AddFragilityFunction(new FragilityFunction(median, beta, $"DS{ls}"));
            }

            if (spec.NumberOfLimitStates > 0)
                result.Add(spec);
        }

        return result;
    }

    /// <summary>
    /// Save a fragility database to CSV.
    /// Useful for exporting custom fragility libraries.
    /// </summary>
    public static void SaveFragilityDatabase(
        string csvPath,
        IReadOnlyList<ComponentFragilitySpec> specs)
    {
        int maxLs = specs.Max(s => s.NumberOfLimitStates);
        if (maxLs == 0) return;

        var colList = new List<string> { "Demand-Type", "Demand-Unit", "Demand-Offset", "Demand-Directional" };
        for (int ls = 1; ls <= maxLs; ls++)
        {
            colList.Add($"LS{ls}-Family");
            colList.Add($"LS{ls}-Theta_0");
            colList.Add($"LS{ls}-Theta_1");
        }
        string[] headers = [.. colList];
        string[] units   = EmptyStrings(headers.Length);

        var rows = specs.Select(spec =>
        {
            var edpType   = spec.Component.GoverningEdp.Type;
            var valueList = new List<string> { edpType, Units.ForEdpType(edpType), "0", "1" };

            for (int ls = 0; ls < maxLs; ls++)
            {
                if (ls < spec.FragilityFunctions.Count)
                {
                    var ff = spec.FragilityFunctions[ls];
                    valueList.Add("lognormal");
                    valueList.Add(ReSilicoCsv.FormatDouble(ff.Median));
                    valueList.Add(ReSilicoCsv.FormatDouble(ff.Beta));
                }
                else
                {
                    valueList.AddRange(["", "", ""]);
                }
            }
            return (key: spec.Component.Id, values: valueList.ToArray());
        });

        var table = ReSilicoCsv.Build(headers, units, rows);
        ReSilicoCsv.Write(csvPath, table);
    }

    // ── Damage sample ─────────────────────────────────────────────────────────

    /// <summary>
    /// Save a damage sample to <c>&lt;prefix&gt;_sample.csv</c>.
    /// Each column = component ID; each row = one realisation's damage state.
    /// </summary>
    public static void SaveDamageSample(string filePrefix, DamageSample sample)
    {
        int n    = sample.Count;
        int nCmp = sample.NumberOfComponents;
        var ids  = sample.ComponentIds.ToArray();

        string[] headers = ids;
        string[] units   = [.. Enumerable.Repeat(Units.Ea, nCmp)];

        var rows = Enumerable.Range(0, n).Select(i =>
        {
            var values = Enumerable.Range(0, nCmp)
                .Select(c => sample.DamageStates[i, c].ToString(CultureInfo.InvariantCulture))
                .ToArray();
            return (key: i.ToString(CultureInfo.InvariantCulture), values);
        });

        var table = ReSilicoCsv.Build(headers, units, rows);
        ReSilicoCsv.Write(filePrefix + "_sample.csv", table);
    }

    /// <summary>
    /// Load a damage sample CSV back into a <see cref="DamageSample"/>.
    /// </summary>
    public static DamageSample LoadDamageSample(string filePrefix)
    {
        string path = filePrefix + "_sample.csv";
        if (!File.Exists(path))
            throw new FileNotFoundException($"Damage sample file not found: {path}");

        var table  = ReSilicoCsv.Read(path);
        int nRows  = table.RowKeys.Count;
        var ids    = table.Headers.ToList();
        var sample = new DamageSample(ids, nRows);

        for (int i = 0; i < nRows; i++)
        {
            string rowKey = table.RowKeys[i];
            for (int c = 0; c < ids.Count; c++)
            {
                string cell = table.Rows[rowKey][c];
                sample.DamageStates[i, c] = int.TryParse(cell, out var v) ? v : 0;
            }
        }

        return sample;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] EmptyStrings(int count)
    {
        var arr = new string[count];
        Array.Fill(arr, string.Empty);
        return arr;
    }
}
