// ReSilico – Probabilistic Damage and Loss Assessment Engine
// SimulationResult: comprehensive statistical postprocessing

using Numerics.Data.Statistics;
using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Loss;

namespace ReSilico.Analysis.Simulation;

/// <summary>
/// Aggregated statistical output from a completed simulation run.
///
/// All statistics are computed on demand from the raw sample arrays.
/// Histograms, exceedance probabilities, and percentiles are all available.
///
/// Percentile convention: probabilities are in [0,1]
/// (e.g., 0.84 = 84th percentile) as required by RMC.Numerics Statistics.Percentile.
/// </summary>
public class SimulationResult
{
    private readonly LossSample _loss;
    private readonly DamageSample _damage;

    internal SimulationResult(LossSample loss, DamageSample damage, int numberOfSimulations)
    {
        _loss = loss;
        _damage = damage;
        NumberOfSimulations = numberOfSimulations;
    }

    // ── Raw samples ───────────────────────────────────────────────────────────

    public LossSample LossSample => _loss;
    public DamageSample DamageSample => _damage;
    public int NumberOfSimulations { get; }

    // ── Total cost statistics ─────────────────────────────────────────────────

    public double MeanCost => Statistics.Mean(_loss.TotalCosts);
    public double StdDevCost => Statistics.StandardDeviation(_loss.TotalCosts);
    public double VarianceCost => Statistics.Variance(_loss.TotalCosts);
    public double CoeffOfVariationCost => StdDevCost / MeanCost;
    public double MedianCost => Statistics.Percentile(_loss.TotalCosts, 0.50);
    public double SkewnessCost => Statistics.Skewness(_loss.TotalCosts);

    /// <summary>
    /// Cost at a given percentile.
    /// <paramref name="p"/> is in [0,1] — e.g., 0.84 = 84th percentile.
    /// </summary>
    public double CostAtPercentile(double p) =>
        Statistics.Percentile(_loss.TotalCosts, Math.Clamp(p, 0.0, 1.0));

    /// <summary>
    /// Fraction of realisations where total cost exceeds <paramref name="threshold"/>.
    /// </summary>
    public double CostExceedanceProbability(double threshold)
    {
        int count = 0;
        foreach (double c in _loss.TotalCosts)
            if (c > threshold) count++;
        return count / (double)NumberOfSimulations;
    }

    // ── Total time statistics ─────────────────────────────────────────────────

    public double MeanTime => Statistics.Mean(_loss.TotalTimes);
    public double StdDevTime => Statistics.StandardDeviation(_loss.TotalTimes);
    public double MedianTime => Statistics.Percentile(_loss.TotalTimes, 0.50);

    public double TimeAtPercentile(double p) =>
        Statistics.Percentile(_loss.TotalTimes, Math.Clamp(p, 0.0, 1.0));

    // ── Casualty statistics ───────────────────────────────────────────────────

    public double MeanFatalities => Statistics.Mean(_loss.Fatalities);
    public double MeanInjuries => Statistics.Mean(_loss.Injuries);

    // ── Damage-state probabilities ────────────────────────────────────────────

    /// <summary>
    /// Empirical probability mass across damage states for a given component.
    /// Returns P(DS = k) for k = 0…maxDS.
    /// </summary>
    public double[] GetDamageStateProbabilities(string componentId, int maxDamageState) =>
        _damage.GetDamageStateProbabilities(componentId, maxDamageState);

    /// <summary>
    /// Empirical exceedance probability P(DS ≥ dsIndex) for a given component.
    /// </summary>
    public double GetExceedanceProbability(string componentId, int dsIndex)
    {
        int col = ((List<string>)_damage.ComponentIds).IndexOf(componentId);
        if (col < 0) throw new ArgumentException($"Component '{componentId}' not found.");

        int count = 0;
        int n = _damage.Count;
        for (int i = 0; i < n; i++)
            if (_damage.DamageStates[i, col] >= dsIndex) count++;
        return count / (double)n;
    }

    // ── Mean loss by component ────────────────────────────────────────────────

    /// <summary>
    /// Expected repair cost per component — identifies the largest contributors.
    /// Returns a dictionary: component ID → mean cost.
    /// </summary>
    public IReadOnlyDictionary<string, double> MeanCostByComponent()
    {
        int nComp = _loss.ComponentIds.Count;
        int n = _loss.Count;
        var result = new Dictionary<string, double>(nComp);

        for (int c = 0; c < nComp; c++)
        {
            double sum = 0.0;
            for (int i = 0; i < n; i++) sum += _loss.ComponentCosts[i, c];
            result[_loss.ComponentIds[c]] = sum / n;
        }
        return result;
    }

    // ── Histogram ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Build an equal-width histogram of total repair costs.
    /// Returns (binEdges[bins+1], counts[bins]).
    /// </summary>
    public (double[] BinEdges, int[] Counts) CostHistogram(int bins = 20)
    {
        double[] costs = _loss.TotalCosts;
        double min = costs.Min(), max = costs.Max();
        if (max <= min) max = min + 1.0;
        double w = (max - min) / bins;

        var counts = new int[bins];
        var edges = new double[bins + 1];
        for (int k = 0; k <= bins; k++) edges[k] = min + k * w;

        foreach (double c in costs)
        {
            int b = Math.Min((int)((c - min) / w), bins - 1);
            counts[b]++;
        }
        return (edges, counts);
    }

    /// <summary>
    /// Mean of the largest fraction of total repair costs (tail-average).
    /// Example: fraction = 0.01 → average of top 1% largest losses.
    /// </summary>
    public double TopKMean(double fraction)
    {
        if (fraction <= 0 || fraction > 1)
            throw new ArgumentOutOfRangeException(nameof(fraction));

        var costs = _loss.TotalCosts;
        int n = costs.Length;

        int k = Math.Max(1, (int)(fraction * n));

        // clone to avoid mutating original samples
        var copy = (double[])costs.Clone();

        Array.Sort(copy); // ascending

        double sum = 0.0;
        for (int i = n - k; i < n; i++)
            sum += copy[i];

        return sum / k;
    }

    /// <summary>
    /// Conditional Value at Risk (CVaR) at level p.
    /// Example: p=0.95 → mean of losses above 95th percentile.
    /// </summary>
    public double CostCVaR(double p)
    {
        if (p <= 0 || p >= 1)
            throw new ArgumentOutOfRangeException(nameof(p));

        var costs = _loss.TotalCosts;
        double threshold = CostAtPercentile(p);

        double sum = 0;
        int count = 0;

        foreach (var c in costs)
        {
            if (c >= threshold)
            {
                sum += c;
                count++;
            }
        }

        return count == 0 ? threshold : sum / count;
    }

    // ── Summary report ────────────────────────────────────────────────────────

    public void PrintSummary(string title = "Assessment Summary")
    {
        string line = new('─', 55);
        Console.WriteLine(line);
        Console.WriteLine($" {title}");
        Console.WriteLine(line);
        Console.WriteLine($"  Simulations     : {NumberOfSimulations:N0}");
        Console.WriteLine();
        Console.WriteLine("  ── Repair Cost ───────────────────────────────");
        Console.WriteLine($"    Mean            : {MeanCost:N2}");
        Console.WriteLine($"    Std-Dev         : {StdDevCost:N2}");
        Console.WriteLine($"    CoV             : {CoeffOfVariationCost:P1}");
        Console.WriteLine($"    Skewness        : {SkewnessCost:F3}");
        Console.WriteLine($"    5th  pctile     : {CostAtPercentile(0.05):N2}");
        Console.WriteLine($"    Median (50th)   : {MedianCost:N2}");
        Console.WriteLine($"    84th pctile     : {CostAtPercentile(0.84):N2}");
        Console.WriteLine($"    95th pctile     : {CostAtPercentile(0.95):N2}");
        Console.WriteLine();
        Console.WriteLine("  ── Repair Time (days) ────────────────────────");
        Console.WriteLine($"    Mean            : {MeanTime:N2}");
        Console.WriteLine($"    Median          : {MedianTime:N2}");
        Console.WriteLine($"    95th pctile     : {TimeAtPercentile(0.95):N2}");
        Console.WriteLine();
        Console.WriteLine("  ── Casualties ────────────────────────────────");
        Console.WriteLine($"    Mean Fatalities : {MeanFatalities:N4}");
        Console.WriteLine($"    Mean Injuries   : {MeanInjuries:N4}");
        Console.WriteLine(line);
    }
}
