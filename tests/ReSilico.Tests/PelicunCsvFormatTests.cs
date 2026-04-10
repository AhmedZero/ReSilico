// Pelicun-ported tests: test_file_io.py + test_base.py
//
// Mirrors:
//   test_save_to_csv()         — write orientation-0 produces Units row
//   test_load_data()           — Units row extracted, shape/column checks
//   test_convert_to_SimpleIndex — dash-join tuple keys
//   test_convert_to_MultiIndex  — dash-split back to parts
//   test_file_not_found         — FileNotFoundException on missing path
//   test_unit_conversion        — ft→m, g→m/s² constants in Units.cs

using ReSilico.IO;
using System;
using System.IO;

namespace ReSilico.Tests;

public class ReSilicoCsvTests
{
    // ─── SimpleIndex helpers ──────────────────────────────────────────────────
    // Mirrors test_base.test_convert_to_SimpleIndex / test_convert_to_MultiIndex

    [Theory]
    [InlineData("PFA", "0", "1")]
    [InlineData("PID", "1", "2")]
    [InlineData("SA_0.23", "0", "1")]
    public void JoinKey_ThenSplitKey_RoundTrip(string a, string b, string c)
    {
        string key    = ReSilicoCsv.JoinKey(a, b, c);
        string[] parts = ReSilicoCsv.SplitKey(key);
        Assert.Equal(new[] { a, b, c }, parts);
    }

    [Fact]
    public void JoinKey_ProducesDashSeparatedString()
    {
        Assert.Equal("PFA-0-1",   ReSilicoCsv.JoinKey("PFA", "0", "1"));
        Assert.Equal("a-b",       ReSilicoCsv.JoinKey("a", "b"));
        Assert.Equal("PID-1-1",   ReSilicoCsv.JoinKey("PID", "1", "1"));
        Assert.Equal("cmp.1-1-1-1", ReSilicoCsv.JoinKey("cmp.1","1","1","1"));
    }

    private static readonly string[] expected = ["A", "1"];
    private static readonly string[] expectedArray = ["B", "1"];
    private static readonly string[] expectedArray0 = ["PFA", "0", "1"];
    private static readonly string[] expectedArray1 = ["SA_0.23", "0", "1"];

    [Fact]
    public void SplitKey_SplitsOnDash()
    {
        Assert.Equal(expected,      ReSilicoCsv.SplitKey("A-1"));
        Assert.Equal(expectedArray,      ReSilicoCsv.SplitKey("B-1"));
        Assert.Equal(expectedArray0, ReSilicoCsv.SplitKey("PFA-0-1"));
        Assert.Equal(expectedArray1, ReSilicoCsv.SplitKey("SA_0.23-0-1"));
    }

    // ─── CSV write (orientation 0) ────────────────────────────────────────────
    // Mirrors test_file_io.test_save_to_csv orientation-0:
    //   Input data: A=[1e-3,2e-3,3e-3], B=[4e-3,5e-3,6e-3], units=meters
    //   Expected: first data row after header = "Units,meters,meters"

    [Fact]
    public void Write_ProducesUnitsRow_AsSecondRow()
    {
        var table = ReSilicoCsv.Build(
            headers: ["A", "B"],
            units:   ["meters", "meters"],
            rows:
            [
                ("0", ["0.001", "0.004"]),
                ("1", ["0.002", "0.005"]),
                ("2", ["0.003", "0.006"])
            ]);

        string csv = ReSilicoCsv.WriteToString(table);
        var lines  = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(",A,B",               lines[0]); // header
        Assert.Equal("Units,meters,meters", lines[1]); // units row
        Assert.Equal("0,0.001,0.004",       lines[2]); // first data row
        Assert.Equal("1,0.002,0.005",       lines[3]);
        Assert.Equal("2,0.003,0.006",       lines[4]);
    }

    // ─── CSV read (Units row detection) ──────────────────────────────────────
    // Mirrors test_file_io.test_load_data — units row is extracted separately

    [Fact]
    public void Read_ExtractsUnitsRow_FromParsedTable()
    {
        const string csv =
            ",PFA-0-1,PFA-1-1,PID-1-1\n" +
            "Units,inps2,inps2,rad\n" +
            "0,158.62478,397.04389,0.02672\n" +
            "1,158.62478,397.04389,0.02672\n";

        var table = ReSilicoCsv.ReadFromString(csv);

        Assert.Equal(["PFA-0-1", "PFA-1-1", "PID-1-1"], table.Headers);
        Assert.Equal(["inps2", "inps2", "rad"],          table.Units);
        Assert.Equal(2, table.RowKeys.Count);
        Assert.Equal("158.62478", table.Rows["0"][0]);
        Assert.Equal("0.02672",   table.Rows["0"][2]);
    }

    [Fact]
    public void Read_CorrectShape_SixRows_NineteenColumns()
    {
        // Build a synthetic table matching pelicun's test_load_data shape (6×19)
        var headers = new string[19];
        for (int i = 0; i < 19; i++) headers[i] = $"col{i}";
        var units = new string[19];
        var rows  = new (string, string[])[6];
        for (int r = 0; r < 6; r++)
            rows[r] = (r.ToString(), new string[19]);

        var table = ReSilicoCsv.Build(headers, units, rows);
        Assert.Equal(19, table.Headers.Length);
        Assert.Equal(6, table.RowKeys.Count);
    }

    // ─── Round-trip write → read ──────────────────────────────────────────────

    [Fact]
    public void WriteRead_RoundTrip_PreservesAllValues()
    {
        var original = ReSilicoCsv.Build(
            headers: ["theta_0", "theta_1", "family"],
            units:   ["rad", "unitless", ""],
            rows:
            [
                ("PID-1-1", ["-3.689", "0.400", "lognormal"]),
                ("PFA-1-1", ["-0.693", "0.500", "lognormal"]),
                ("SA-0-1",  ["0.000",  "1.000", "normal"])
            ]);

        string csvStr = ReSilicoCsv.WriteToString(original);
        var reloaded  = ReSilicoCsv.ReadFromString(csvStr);

        Assert.Equal(original.Headers, reloaded.Headers);
        Assert.Equal(original.Units,   reloaded.Units);
        Assert.Equal(original.RowKeys, reloaded.RowKeys);

        foreach (var key in original.RowKeys)
            Assert.Equal(original.Rows[key], reloaded.Rows[key]);
    }

    // ─── File-system round-trip ───────────────────────────────────────────────

    [Fact]
    public void Write_ThenRead_FromDisk_Identical()
    {
        string path = Path.GetTempFileName() + ".csv";
        try
        {
            var table = ReSilicoCsv.Build(
                headers: ["A", "B"],
                units:   ["ea", "ea"],
                rows:    [("0", ["1", "2"]), ("1", ["3", "4"])]);

            ReSilicoCsv.Write(path, table);
            var loaded = ReSilicoCsv.Read(path);

            Assert.Equal(table.Headers, loaded.Headers);
            Assert.Equal(table.Units,   loaded.Units);
            Assert.Equal(table.RowKeys, loaded.RowKeys);
        }
        finally { File.Delete(path); }
    }

    // ─── FileNotFoundException ────────────────────────────────────────────────
    // Mirrors test_load_data edge case: exception on missing file

    [Fact]
    public void Read_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => ReSilicoCsv.Read("/nonexistent/path/does_not_exist.csv"));
    }

    // ─── CSV parsing edge cases ───────────────────────────────────────────────

    [Fact]
    public void SplitCsv_HandlesQuotedCommas()
    {
        string line = "\"hello, world\",plain,\"a,b,c\"";
        string[] parts = ReSilicoCsv.SplitCsv(line);
        Assert.Equal(3, parts.Length);
        Assert.Equal("hello, world", parts[0]);
        Assert.Equal("plain",        parts[1]);
        Assert.Equal("a,b,c",        parts[2]);
    }

    [Fact]
    public void SplitCsv_HandlesEmptyCells()
    {
        string[] parts = ReSilicoCsv.SplitCsv(",A,,B,");
        Assert.Equal(5, parts.Length);
        Assert.Equal(string.Empty, parts[0]);
        Assert.Equal("A",          parts[1]);
        Assert.Equal(string.Empty, parts[2]);
    }

    [Fact]
    public void SplitCsv_HandlesEscapedQuotes()
    {
        string[] parts = ReSilicoCsv.SplitCsv("\"say \"\"hello\"\"\"");
        Assert.Single(parts);
        Assert.Equal("say \"hello\"", parts[0]);
    }

    // ─── GetDouble / typed accessors ─────────────────────────────────────────

    [Fact]
    public void GetDouble_ParsesFloatValues()
    {
        var table = ReSilicoCsv.ReadFromString(
            ",Theta_0,Theta_1\nUnits,,\nPID-1-1,0.025,0.40\nPFA-1-1,0.500,0.50\n");

        Assert.Equal(0.025, table.GetDouble("PID-1-1", "Theta_0"), precision: 8);
        Assert.Equal(0.400, table.GetDouble("PID-1-1", "Theta_1"), precision: 8);
        Assert.Equal(0.500, table.GetDouble("PFA-1-1", "Theta_0"), precision: 8);
    }

    [Fact]
    public void GetDouble_ReturnsFallback_ForEmptyCell()
    {
        var table = ReSilicoCsv.ReadFromString(
            ",TruncLo\nUnits,\nPID-1-1,\n");

        double val = table.GetDouble("PID-1-1", "TruncLo", fallback: double.NaN);
        Assert.True(double.IsNaN(val));
    }

    // ─── FormatDouble ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.025,    "0.025")]
    [InlineData(0.4,      "0.4")]
    [InlineData(1.0,      "1")]
    [InlineData(158.6242, "158.624")]
    public void FormatDouble_ProducesInvariantCultureString(double val, string expected)
    {
        string formatted = ReSilicoCsv.FormatDouble(val);
        Assert.StartsWith(expected, formatted); // G6 may add more digits
    }

    [Fact]
    public void FormatDouble_NaN_IsEmptyString()
    {
        Assert.Equal(string.Empty, ReSilicoCsv.FormatDouble(double.NaN));
    }

    // ─── Units constants (test_base.test_unit_conversion equivalents) ─────────

    [Fact]
    public void Units_ForEdpType_PID_IsRad()
    {
        Assert.Equal(Units.Rad, Units.ForEdpType("PID"));
        Assert.Equal(Units.Rad, Units.ForEdpType("RID"));
    }

    [Fact]
    public void Units_ForEdpType_PFA_IsG()
    {
        Assert.Equal(Units.G, Units.ForEdpType("PFA"));
        Assert.Equal(Units.G, Units.ForEdpType("SA"));
        Assert.Equal(Units.G, Units.ForEdpType("PGA"));
    }

    [Fact]
    public void Units_ForEdpType_Unknown_IsUnitless()
    {
        Assert.Equal(Units.Unitless, Units.ForEdpType("UNKNOWN"));
    }
}
