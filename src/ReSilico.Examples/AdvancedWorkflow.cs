// ReSilico – Probabilistic Damage and Loss Assessment Engine
// AdvancedWorkflow: full example showcasing the SimulationPipeline API,
//   LHS sampling, correlated EDPs, batch fragility, and rich statistics output.
//
// Scenario: 6-story steel moment frame (SMF) office building
//   • 2 bays in X (N-S), 2 bays in Y (E-W)
//   • Sa(T₁=1.5s) = 0.35g design earthquake
//   • EDPs: PID stories 1–6 (X), PFA stories 0–6 (X)
//   • Components: SMF structural (B1035), NS drift-sensitive (C3032),
//                 NS accel-sensitive (D3041), glazing (B2011)
//   • Decision variables: cost and time

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;

namespace ReSilico.Examples;

public static class AdvancedWorkflow
{
    public static void Run()
    {
        const int N = 10_000;
        const int Seed = 2024;
        const int Stories = 6;

        Console.WriteLine("ReSilico – Advanced Pipeline Example");
        Console.WriteLine($"6-Story Steel Moment Frame, Sa(1.5s)=0.35g, N={N:N0} simulations");
        Console.WriteLine();

        // ── 1. Asset definition ───────────────────────────────────────────────
        var asset = new Asset("SMF-6", "6-Story Steel Office", HazardType.Earthquake)
        {
            ReplacementCost = 12_000_000,
            FloorArea = 5_000,
            NumberOfStories = Stories,
            OccupancyType = "Office",
        };

        // EDPs
        var pidEdps = Enumerable.Range(1, Stories).Select(s => new EDP("PID", s, 1, "rad")).ToArray();
        var pfaEdps = Enumerable.Range(0, Stories + 1).Select(s => new EDP("PFA", s, 1, "g")).ToArray();

        // ── 2. Components ─────────────────────────────────────────────────────
        // Structural frames  — 2 bays × 2 directions = 4 per story.
        for (int s = 1; s <= Stories; s++)
        {
            var c = new Component($"B1035-{s}", pidEdps[s - 1], quantity: 4, location: s);
            c.AddDamageState(new DamageState(0, "No Damage"));
            c.AddDamageState(new DamageState(1, "Minor Yielding"));
            c.AddDamageState(new DamageState(2, "Moderate", "Significant residual drift"));
            c.AddDamageState(new DamageState(3, "Collapse Mechanism"));
            asset.AddComponent(c);
        }
        // Non-structural drift-sensitive (partitions/cladding)
        for (int s = 1; s <= Stories; s++)
        {
            var c = new Component($"C3032-{s}", pidEdps[s - 1], quantity: 1, location: s);
            c.AddDamageState(new DamageState(0, "No Damage"));
            c.AddDamageState(new DamageState(1, "Cracking"));
            c.AddDamageState(new DamageState(2, "Severe Cracking"));
            asset.AddComponent(c);
        }
        // Glazing (exterior) — drift-sensitive
        for (int s = 1; s <= Stories; s++)
        {
            var c = new Component($"B2011-{s}", pidEdps[s - 1], quantity: 1, location: s);
            c.AddDamageState(new DamageState(0, "Intact"));
            c.AddDamageState(new DamageState(1, "Cracked / Fallen"));
            asset.AddComponent(c);
        }
        // Accel-sensitive (HVAC, electrical, sprinklers)
        for (int s = 0; s <= Stories; s++)
        {
            var c = new Component($"D3041-{s}", pfaEdps[s], quantity: 1, location: s);
            c.AddDamageState(new DamageState(0, "No Damage"));
            c.AddDamageState(new DamageState(1, "Functional Damage"));
            asset.AddComponent(c);
        }

        // ── 3. Demand model ───────────────────────────────────────────────────
        var demandModel = new DemandModel();

        // PID medians and dispersions (stories 1–6, X-direction)
        double[] pidMedians = [0.003, 0.005, 0.007, 0.008, 0.007, 0.005];
        double[] pidBetas   = [0.40, 0.38, 0.35, 0.35, 0.38, 0.40];

        for (int s = 0; s < Stories; s++)
            demandModel.AddEdp(new EdpDistributionSpec(
                pidEdps[s], EdpDistributionKind.Lognormal,
                Math.Log(pidMedians[s]), pidBetas[s], TruncLower: 0.0));

        // PFA medians (ground + 6 floors)
        double[] pfaMedians = [0.20, 0.28, 0.36, 0.44, 0.50, 0.54, 0.58];
        double[] pfaBetas   = [0.30, 0.32, 0.33, 0.33, 0.32, 0.32, 0.30];

        for (int s = 0; s <= Stories; s++)
            demandModel.AddEdp(new EdpDistributionSpec(
                pfaEdps[s], EdpDistributionKind.Lognormal,
                Math.Log(pfaMedians[s]), pfaBetas[s], TruncLower: 0.0));

        // Full 13×13 correlation matrix (PID1–6, PFA0–6)
        demandModel.SetCorrelation(BuildCorrelationMatrix(Stories));

        // ── 4. Damage model ───────────────────────────────────────────────────
        var damageModel = new DamageModel();

        // Structural B1035 fragility (FEMA P-58 SMF values)
        double[] bMedians = [0.006, 0.020, 0.050];  // DS1, DS2, DS3
        double bBeta = 0.35;
        for (int s = 1; s <= Stories; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"B1035-{s}");
            var spec = new ComponentFragilitySpec(comp);
            foreach (double m in bMedians)
                spec.AddFragilityFunction(new FragilityFunction(m, bBeta));
            damageModel.Add(spec);
        }

        // Partition/cladding C3032
        double[] cMedians = [0.005, 0.015];
        for (int s = 1; s <= Stories; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"C3032-{s}");
            var spec = new ComponentFragilitySpec(comp);
            foreach (double m in cMedians)
                spec.AddFragilityFunction(new FragilityFunction(m, 0.50));
            damageModel.Add(spec);
        }

        // Glazing B2011
        for (int s = 1; s <= Stories; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"B2011-{s}");
            var spec = new ComponentFragilitySpec(comp);
            spec.AddFragilityFunction(new FragilityFunction(0.010, 0.45));
            damageModel.Add(spec);
        }

        // HVAC/electrical D3041
        for (int s = 0; s <= Stories; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"D3041-{s}");
            var spec = new ComponentFragilitySpec(comp);
            spec.AddFragilityFunction(new FragilityFunction(0.60, 0.40));
            damageModel.Add(spec);
        }

        // ── 5. Loss model ─────────────────────────────────────────────────────
        var lossModel = new LossModel();

        // Structural B1035 — cost in $/bay, time in days
        (double cost, double time)[] bConseq = [(0, 0), (25_000, 10), (120_000, 45), (550_000, 180)];
        for (int s = 1; s <= Stories; s++)
        {
            string id = $"B1035-{s}";
            for (int ds = 1; ds <= 3; ds++)
            {
                lossModel.AddConsequence(id, new ConsequenceFunction(ds, DecisionVariable.Cost, bConseq[ds].cost, beta: 0.40));
                lossModel.AddConsequence(id, new ConsequenceFunction(ds, DecisionVariable.Time, bConseq[ds].time, beta: 0.30));
            }
        }

        // Partition/cladding
        (double cost, double time)[] cConseq = [(0, 0), (6_000, 3), (30_000, 12)];
        for (int s = 1; s <= Stories; s++)
        {
            string id = $"C3032-{s}";
            for (int ds = 1; ds <= 2; ds++)
            {
                lossModel.AddConsequence(id, new ConsequenceFunction(ds, DecisionVariable.Cost, cConseq[ds].cost, beta: 0.50));
                lossModel.AddConsequence(id, new ConsequenceFunction(ds, DecisionVariable.Time, cConseq[ds].time, beta: 0.40));
            }
        }

        // Glazing
        for (int s = 1; s <= Stories; s++)
        {
            string id = $"B2011-{s}";
            lossModel.AddConsequence(id, new ConsequenceFunction(1, DecisionVariable.Cost, 12_000, beta: 0.50));
            lossModel.AddConsequence(id, new ConsequenceFunction(1, DecisionVariable.Time, 5, beta: 0.40));
        }

        // HVAC/electrical
        for (int s = 0; s <= Stories; s++)
        {
            string id = $"D3041-{s}";
            lossModel.AddConsequence(id, new ConsequenceFunction(1, DecisionVariable.Cost, 15_000, beta: 0.45));
            lossModel.AddConsequence(id, new ConsequenceFunction(1, DecisionVariable.Time, 7, beta: 0.35));
        }

        // ── 6. Run via SimulationPipeline ─────────────────────────────────────
        var sw = System.Diagnostics.Stopwatch.StartNew();

        SimulationResult result = new SimulationPipeline(asset)
            .WithDemand(demandModel)
            .WithDamage(damageModel)
            .WithLoss(lossModel)
            .WithSampler(LatinHypercubeSampler.Standard)
            .WithDecisionVariables(DecisionVariable.Cost | DecisionVariable.Time)
            .Run(N, Seed);

        sw.Stop();
        Console.WriteLine($"Completed in {sw.ElapsedMilliseconds} ms.\n");

        // ── 7. Output ─────────────────────────────────────────────────────────
        result.PrintSummary("6-Story SMF Office – Sa(1.5s)=0.35g");
        Console.WriteLine();

        // Structural exceedance by story
        Console.WriteLine("  Structural Damage Exceedance Probabilities:");
        Console.WriteLine($"    {"Story",-6} {"P(≥DS1)",8} {"P(≥DS2)",8} {"P(≥DS3)",8}");
        for (int s = 1; s <= Stories; s++)
        {
            string id = $"B1035-{s}";
            double p1 = result.GetExceedanceProbability(id, 1);
            double p2 = result.GetExceedanceProbability(id, 2);
            double p3 = result.GetExceedanceProbability(id, 3);
            Console.WriteLine($"    {s,-6} {p1,8:P2} {p2,8:P2} {p3,8:P2}");
        }
        Console.WriteLine();

        // Collapse proxy
        double pCollapse = Enumerable.Range(1, Stories)
            .Max(s => result.GetExceedanceProbability($"B1035-{s}", 3));
        Console.WriteLine($"  P(Collapse proxy, max story DS3)  : {pCollapse:P2}");
        Console.WriteLine($"  P(Cost > $1M)                     : {result.CostExceedanceProbability(1_000_000):P2}");
        Console.WriteLine($"  P(Cost > $3M)                     : {result.CostExceedanceProbability(3_000_000):P2}");
        Console.WriteLine();

        // Top 5 components by expected loss
        Console.WriteLine("  Top 5 Components by Expected Repair Cost:");
        var byComp = result.MeanCostByComponent()
            .OrderByDescending(kv => kv.Value)
            .Take(5);
        foreach (var (id, cost) in byComp)
            Console.WriteLine($"    {id,-12}: {cost:N0}");
        Console.WriteLine();

        // Histogram
        Console.WriteLine("  Repair Cost Distribution:");
        var (edges, counts) = result.CostHistogram(bins: 12);
        for (int b = 0; b < counts.Length; b++)
        {
            double frac = counts[b] / (double)N;
            string bar = new('█', (int)(frac * 36));
            Console.WriteLine($"    [{edges[b],8:N0}–{edges[b + 1],8:N0}] {bar,-36} {frac:P1}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a 13×13 correlation matrix for [PID1..6, PFA0..6].
    /// Strong within-type spatial correlation, moderate cross-type coupling.
    /// </summary>
    private static double[,] BuildCorrelationMatrix(int stories)
    {
        int nPID = stories;
        int nPFA = stories + 1;
        int n = nPID + nPFA;
        var rho = new double[n, n];
        for (int i = 0; i < n; i++) rho[i, i] = 1.0;

        // Within PID (indices 0..5): exponential spatial correlation
        for (int i = 0; i < nPID; i++)
            for (int j = 0; j < nPID; j++)
                if (i != j) rho[i, j] = Math.Exp(-0.40 * Math.Abs(i - j));

        // Within PFA (indices 6..12)
        for (int i = 0; i < nPFA; i++)
            for (int j = 0; j < nPFA; j++)
                if (i != j) rho[nPID + i, nPID + j] = Math.Exp(-0.35 * Math.Abs(i - j));

        // Cross-type PID↔PFA = 0.40 (moderate)
        for (int i = 0; i < nPID; i++)
            for (int j = 0; j < nPFA; j++)
            {
                rho[i, nPID + j] = 0.40;
                rho[nPID + j, i] = 0.40;
            }

        return rho;
    }
}
