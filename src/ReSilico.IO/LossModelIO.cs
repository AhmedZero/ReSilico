// ReSilico.IO – Loss Model IO
//
// Loss database schema:
//
//   Index: "<componentId>-<DV>"   e.g. "B1033.001a-Cost"
//   Cols:  DV-Unit,
//          DS1-Family, DS1-Theta_0, DS1-Theta_1,
//          DS2-Family, DS2-Theta_0, DS2-Theta_1,  ...
//
// Loss sample schema (<prefix>_sample.csv):
//
//   Header row: "Cost-Total", "Time-Total", then "Cost-<compId>" per component
//   Units row:  "USD", "worker_day", then "USD" per component
//   Data rows:  realisation index → total cost / total time / per-component costs

using ReSilico.Analysis.Loss;
using ReSilico.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ReSilico.IO;

public static class LossModelIO
{
    // ── Loss database ─────────────────────────────────────────────────────────

    /// <summary>
    /// Load a loss (consequence) database CSV.
    ///
    /// Returns a list of <c>(componentId, ConsequenceFunction)</c> tuples
    /// ready to be passed to <see cref="LossModel.AddConsequence"/>.
    /// </summary>
    public static List<(string componentId, ConsequenceFunction fn)> LoadLossDatabase(
        string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"Loss database not found: {csvPath}");

        var table  = ReSilicoCsv.Read(csvPath);
        var result = new List<(string, ConsequenceFunction)>();

        foreach (var rowKey in table.RowKeys)
        {
            // Row key format: "<componentId>-<DV>"  e.g. "B1033.001a-Cost"
            int lastDash = rowKey.LastIndexOf('-');
            if (lastDash < 0) continue;

            string compId = rowKey[..lastDash];
            string dvStr  = rowKey[(lastDash + 1)..];

            var dv = dvStr.Equals("Cost", StringComparison.OrdinalIgnoreCase)
                ? DecisionVariable.Cost
                : dvStr.Equals("Time", StringComparison.OrdinalIgnoreCase)
                ? DecisionVariable.Time
                : dvStr.Equals("Casualties", StringComparison.OrdinalIgnoreCase)
                ? DecisionVariable.Casualties
                : DecisionVariable.Cost; // fallback

            for (int ds = 1; ; ds++)
            {
                string familyCol = $"DS{ds}-Family";
                string theta0Col = $"DS{ds}-Theta_0";
                string theta1Col = $"DS{ds}-Theta_1";

                if (!table.HasColumn(familyCol)) break;

                var family = table.Get(rowKey, familyCol);
                if (string.IsNullOrWhiteSpace(family)) break;

                double medianLoss = table.GetDouble(rowKey, theta0Col);
                if (double.IsNaN(medianLoss)) break;

                double beta = table.GetDouble(rowKey, theta1Col, 0.0);

                result.Add((compId, new ConsequenceFunction(
                    damageStateIndex: ds,
                    decisionVariable: dv,
                    medianLoss: medianLoss,
                    beta: double.IsNaN(beta) ? 0.0 : beta)));
            }
        }

        return result;
    }

    /// <summary>
    /// Save a loss database to CSV.
    /// <paramref name="lossEntries"/> = sequence of (componentId, ConsequenceFunction).
    /// </summary>
    public static void SaveLossDatabase(
        string csvPath,
        IEnumerable<(string componentId, ConsequenceFunction fn)> lossEntries)
    {
        var grouped = lossEntries
            .GroupBy(e => (e.componentId, DvLabel(e.fn.DecisionVariable)))
            .ToList();

        int maxDs = grouped.Max(g => g.Max(e => e.fn.DamageStateIndex));

        var colList = new List<string> { "DV-Unit" };
        for (int ds = 1; ds <= maxDs; ds++)
        {
            colList.Add($"DS{ds}-Family");
            colList.Add($"DS{ds}-Theta_0");
            colList.Add($"DS{ds}-Theta_1");
        }
        string[] headers = [.. colList];
        string[] units   = EmptyStrings(headers.Length);

        var rows = grouped.Select(g =>
        {
            var (compId, dvLabel) = g.Key;
            string rowKey = $"{compId}-{dvLabel}";
            string dvUnit = dvLabel == "Cost" ? Units.Usd : Units.WorkerDay;

            var valueList = new List<string> { dvUnit };
            var fnByDs    = g.ToDictionary(e => e.fn.DamageStateIndex, e => e.fn);

            for (int ds = 1; ds <= maxDs; ds++)
            {
                if (fnByDs.TryGetValue(ds, out var fn))
                {
                    valueList.Add("lognormal");
                    valueList.Add(ReSilicoCsv.FormatDouble(fn.MedianLoss));
                    valueList.Add(ReSilicoCsv.FormatDouble(fn.Beta));
                }
                else
                {
                    valueList.AddRange(["", "", ""]);
                }
            }
            return (key: rowKey, values: valueList.ToArray());
        });

        var table = ReSilicoCsv.Build(headers, units, rows);
        ReSilicoCsv.Write(csvPath, table);
    }

    // ── Loss sample ───────────────────────────────────────────────────────────

    /// <summary>
    /// Save a loss sample to <c>&lt;prefix&gt;_sample.csv</c>.
    /// Columns: Cost-Total, Time-Total, then Cost-&lt;compId&gt; per component.
    /// </summary>
    public static void SaveLossSample(string filePrefix, LossSample sample)
    {
        int n    = sample.Count;
        int nCmp = sample.ComponentIds.Count;
        var ids  = sample.ComponentIds;

        var headers = new List<string> { "Cost-Total", "Time-Total" };
        headers.AddRange(ids.Select(id => $"Cost-{id}"));

        var unitList = new List<string> { Units.Usd, Units.WorkerDay };
        unitList.AddRange(Enumerable.Repeat(Units.Usd, nCmp));

        var rows = Enumerable.Range(0, n).Select(i =>
        {
            var values = new List<string>
            {
                ReSilicoCsv.FormatDouble(sample.TotalCosts[i]),
                ReSilicoCsv.FormatDouble(sample.TotalTimes[i])
            };
            for (int c = 0; c < nCmp; c++)
                values.Add(ReSilicoCsv.FormatDouble(sample.ComponentCosts[i, c]));
            return (key: i.ToString(CultureInfo.InvariantCulture), values: values.ToArray());
        });

        var table = ReSilicoCsv.Build([.. headers], [.. unitList], rows);
        ReSilicoCsv.Write(filePrefix + "_sample.csv", table);
    }

    /// <summary>
    /// Load a loss sample CSV back into a <see cref="LossSample"/>.
    /// </summary>
    public static LossSample LoadLossSample(string filePrefix)
    {
        string path = filePrefix + "_sample.csv";
        if (!File.Exists(path))
            throw new FileNotFoundException($"Loss sample file not found: {path}");

        var table   = ReSilicoCsv.Read(path);
        int n       = table.RowKeys.Count;
        var compIds = table.Headers
            .Where(h => h.StartsWith("Cost-", StringComparison.OrdinalIgnoreCase)
                     && h != "Cost-Total")
            .Select(h => h["Cost-".Length..])
            .ToList();

        var sample = new LossSample(compIds, n);

        for (int i = 0; i < n; i++)
        {
            string rowKey = table.RowKeys[i];
            sample.TotalCosts[i] = ParseDouble(table, rowKey, "Cost-Total");
            sample.TotalTimes[i] = ParseDouble(table, rowKey, "Time-Total");
            for (int c = 0; c < compIds.Count; c++)
                sample.ComponentCosts[i, c] = ParseDouble(table, rowKey, $"Cost-{compIds[c]}");
        }

        return sample;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double ParseDouble(ReSilicoTable t, string row, string col)
    {
        var s = t.HasColumn(col) ? t.Get(row, col) : string.Empty;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : 0.0;
    }

    private static string DvLabel(DecisionVariable dv) => dv switch
    {
        DecisionVariable.Time       => "Time",
        DecisionVariable.Casualties => "Casualties",
        _                           => "Cost"
    };

    private static string[] EmptyStrings(int count)
    {
        var arr = new string[count];
        Array.Fill(arr, string.Empty);
        return arr;
    }
}
