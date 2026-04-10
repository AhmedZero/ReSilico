// ReSilico.IO – ReSilico CSV format
//
// All ReSilico data files share the same two-row header convention:
//   Row 0: column names (first cell is empty — that's the index label)
//   Row 1: "Units" — one unit string per column, empty if unitless
//   Row 2+: data rows; first cell is the row index (string key)
//
// Multi-level indices are stored as dash-separated strings, e.g.
//   "PID-1-1"  ←→  (type="PID", location="1", direction="1")
//
// This class is a static helper that handles raw text ↔ string[,] bridging.
// Higher-level readers (DemandModelIO, DamageModelIO, etc.) call it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ReSilico.IO;

/// <summary>
/// A parsed ReSilico CSV table:
///   <see cref="Headers"/>  – column names (excluding the index column).
///   <see cref="Units"/>    – one unit string per column (empty string if none).
///   <see cref="Rows"/>     – dictionary from row-key → column values (as strings).
/// </summary>
public sealed class ReSilicoTable(string[] headers, string[] units)
{
    public string[] Headers { get; } = headers;
    public string[] Units { get; } = units;
    /// <summary>Ordered list of row keys (preserves file order).</summary>
    public List<string> RowKeys { get; } = [];
    /// <summary>Row data by index key → array of column values (parallel to Headers).</summary>
    public Dictionary<string, string[]> Rows { get; } = [];

    // ── Typed accessors ──────────────────────────────────────────────────────

    public string Get(string rowKey, string column)
    {
        int c = Array.IndexOf(Headers, column);
        if (c < 0) throw new KeyNotFoundException($"Column '{column}' not found.");
        return Rows.TryGetValue(rowKey, out var row) ? row[c] : string.Empty;
    }

    public double GetDouble(string rowKey, string column, double fallback = double.NaN)
    {
        var s = Get(rowKey, column);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public bool HasColumn(string column) => Array.IndexOf(Headers, column) >= 0;
    public bool HasRow(string key) => Rows.ContainsKey(key);

    // ── Ordered enumeration ──────────────────────────────────────────────────
    public IEnumerable<(string key, string[] values)> AllRows()
    {
        foreach (var key in RowKeys)
            yield return (key, Rows[key]);
    }
}

/// <summary>
/// Reads and writes ReSilico CSV files used by ReSilico's IO layer.
/// </summary>
public static class ReSilicoCsv
{
    // ── Read ─────────────────────────────────────────────────────────────────

    public static ReSilicoTable Read(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            return ReadCore(_ => reader.ReadLine(), filePath);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath, ex);
        }
    }

    public static ReSilicoTable ReadFromString(string csvContent)
    {
        using var reader = new StringReader(csvContent);
        return ReadCore(_ => reader.ReadLine(), "<string>");
    }

    private static ReSilicoTable ReadCore(Func<string?, string?> readLine, string path)
    {
        // ── Header row ───────────────────────────────────────────────────────
        var headerLine = readLine(null)
            ?? throw new InvalidDataException($"Empty file: {path}");

        var headerCells = SplitCsv(headerLine);
        if (headerCells.Length < 2)
            throw new InvalidDataException($"Header must have at least 2 columns: {path}");

        // First cell is the (unnamed) index column label — ignore it.
        var headers = headerCells[1..]; // column names

        // ── Units row ────────────────────────────────────────────────────────
        var unitsLine = readLine(null)
            ?? throw new InvalidDataException($"Missing Units row: {path}");

        var unitsCells = SplitCsv(unitsLine);
        string[] units;
        bool unitsRowConsumed;

        if (unitsCells.Length > 0 &&
            unitsCells[0].Trim().Equals("Units", StringComparison.OrdinalIgnoreCase))
        {
            units = PadOrTrim(unitsCells[1..], headers.Length);
            unitsRowConsumed = true;
        }
        else
        {
            // No Units row — treat the line as the first data row.
            units = EmptyStrings(headers.Length);
            unitsRowConsumed = false;
        }

        var table = new ReSilicoTable(headers, units);

        // If the "units line" was actually a data row, process it first.
        if (!unitsRowConsumed)
            AddDataRow(table, unitsCells, headers.Length);

        // ── Data rows ────────────────────────────────────────────────────────
        string? line;
        while ((line = readLine(null)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            AddDataRow(table, SplitCsv(line), headers.Length);
        }

        return table;
    }

    private static void AddDataRow(ReSilicoTable table, string[] cells, int columnCount)
    {
        if (cells.Length == 0) return;
        var key = cells[0].Trim();
        if (string.IsNullOrEmpty(key)) return;
        var values = PadOrTrim(cells[1..], columnCount);
        if (!table.Rows.ContainsKey(key))
            table.RowKeys.Add(key);
        table.Rows[key] = values;
    }

    // ── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Write a ReSilico table to a CSV file.
    /// <paramref name="indexColumnName"/> is the header of the first (index) column;
    /// typically left empty.
    /// </summary>
    public static void Write(
        string filePath,
        ReSilicoTable table,
        string indexColumnName = "")
    {
        using var writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        WriteCore(writer, table, indexColumnName);
    }

    /// <summary>Write a ReSilico table to a string (LF line endings).</summary>
    public static string WriteToString(ReSilicoTable table, string indexColumnName = "")
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        writer.NewLine = "\n";
        WriteCore(writer, table, indexColumnName);
        return sb.ToString();
    }

    private static void WriteCore(TextWriter writer, ReSilicoTable table, string indexColumnName)
    {
        writer.WriteLine(BuildCsvLine(Prepend(indexColumnName, table.Headers)));
        writer.WriteLine(BuildCsvLine(Prepend("Units", table.Units)));
        foreach (var (key, values) in table.AllRows())
            writer.WriteLine(BuildCsvLine(Prepend(key, values)));
    }

    // ── Builder ──────────────────────────────────────────────────────────────

    /// <summary>Create a <see cref="ReSilicoTable"/> from parallel arrays.</summary>
    public static ReSilicoTable Build(
        string[] headers,
        string[] units,
        IEnumerable<(string key, string[] values)> rows)
    {
        var table = new ReSilicoTable(headers, units);
        foreach (var (key, values) in rows)
        {
            table.RowKeys.Add(key);
            table.Rows[key] = PadOrTrim(values, headers.Length);
        }
        return table;
    }

    // ── Low-level CSV helpers ─────────────────────────────────────────────────

    /// <summary>RFC-4180-compatible CSV split (handles quoted fields).</summary>
    public static string[] SplitCsv(string line)
    {
        var fields  = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return [.. fields];
    }

    private static string BuildCsvLine(string[] cells)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var cell = cells[i] ?? string.Empty;
            if (cell.Contains(',') || cell.Contains('"') || cell.Contains('\n'))
            {
                sb.Append('"');
                sb.Append(cell.Replace("\"", "\"\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(cell);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Return <paramref name="src"/> trimmed to <paramref name="length"/> elements,
    /// or padded with <see cref="string.Empty"/> if shorter.
    /// </summary>
    private static string[] PadOrTrim(string[] src, int length)
    {
        if (src.Length == length)
        {
            // Trim in-place copy so callers always get trimmed values.
            var same = new string[length];
            for (int i = 0; i < length; i++)
                same[i] = (src[i] ?? string.Empty).Trim();
            return same;
        }

        var result = new string[length];
        int copy = Math.Min(src.Length, length);
        for (int i = 0; i < copy; i++)
            result[i] = (src[i] ?? string.Empty).Trim();
        for (int i = copy; i < length; i++)
            result[i] = string.Empty; // pad remainder — never leave nulls
        return result;
    }

    private static string[] Prepend(string first, string[] rest)
    {
        var result = new string[rest.Length + 1];
        result[0] = first;
        Array.Copy(rest, 0, result, 1, rest.Length);
        return result;
    }

    // ── SimpleIndex helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Split a dash-separated index key.
    /// "PID-1-1" → ["PID", "1", "1"]
    /// </summary>
    public static string[] SplitKey(string key) => key.Split('-');

    /// <summary>
    /// Join parts into a dash-separated index key.
    /// ("PID", "1", "1") → "PID-1-1"
    /// </summary>
    public static string JoinKey(params string[] parts) => string.Join('-', parts);

    // ── Number formatting ─────────────────────────────────────────────────────

    /// <summary>
    /// Format a double for CSV output. Returns an empty string for NaN.
    /// Uses G9 to preserve round-trip precision for typical loss/EDP values.
    /// </summary>
    public static string FormatDouble(double value)
        => double.IsNaN(value) ? string.Empty
         : value.ToString("G9", CultureInfo.InvariantCulture);

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static string[] EmptyStrings(int count)
    {
        var arr = new string[count];
        Array.Fill(arr, string.Empty);
        return arr;
    }
}
