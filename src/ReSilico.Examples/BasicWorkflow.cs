// ReSilico – Probabilistic Damage and Loss Assessment Engine
// BasicWorkflow: 10,000-simulation end-to-end earthquake loss assessment example
//
// Scenario: 4-story RC moment frame office building
//
// EDPs:      Peak Inter-story Drift (PID) at stories 1–4 in the X-direction
// Hazard:    Sa(T₁ = 1 s) = 0.5g  (design earthquake)
// Components: Structural (B1033), non-structural drift-sensitive (C3011),
//             non-structural acceleration-sensitive (D2021)
// Decision variables: Repair cost, downtime (days)

using ReSilico.Analysis.Damage;
using ReSilico.Analysis.Demand;
using ReSilico.Analysis.Loss;
using ReSilico.Analysis.Simulation;
using ReSilico.Core.Sampling;
using ReSilico.Domain;
using ReSilico.Domain.Enums;

namespace ReSilico.Examples;

public static class BasicWorkflow
{
    public static void Run()
    {
        Console.WriteLine("ReSilico – Probabilistic Loss Assessment");
        Console.WriteLine("Scenario: 4-Story RC Office Building, Sa(1s) = 0.5g");
        Console.WriteLine();

        const int N = 10_000;
        const int Seed = 42;

        // ─── Step 1: Define the Asset ────────────────────────────────────────
        var asset = new Asset("B01", "4-Story RC Office", HazardType.Earthquake)
        {
            ReplacementCost = 5_000_000,  // $5 M
            FloorArea = 2_500,            // 2,500 m²
            NumberOfStories = 4,
            OccupancyType = "Commercial/Office",
        };

        // ─── Step 2: Define EDPs ─────────────────────────────────────────────
        // Story-level PID (Peak Inter-story Drift) in X-direction.
        // PFA (Peak Floor Acceleration) at each floor.
        var pidEdps = Enumerable.Range(1, 4)
            .Select(story => new EDP("PID", story, 1, "rad"))
            .ToArray();
        var pfaEdps = Enumerable.Range(1, 4)
            .Select(story => new EDP("PFA", story, 1, "g"))
            .ToArray();

        // ─── Step 3: Define Components and add to Asset ──────────────────────
        // Structural frames — governed by PID (story drift)
        for (int s = 1; s <= 4; s++)
        {
            var comp = new Component($"B1033-{s}", pidEdps[s - 1], quantity: 2, location: s, direction: 1);
            comp.AddDamageState(new DamageState(0, "No Damage"));
            comp.AddDamageState(new DamageState(1, "Minor", "Hairline cracks in beams"));
            comp.AddDamageState(new DamageState(2, "Moderate", "Plastic hinging at beam ends"));
            comp.AddDamageState(new DamageState(3, "Severe", "Column damage, partial loss of capacity"));
            asset.AddComponent(comp);
        }

        // Non-structural drift-sensitive (partitions/cladding) — governed by PID
        for (int s = 1; s <= 4; s++)
        {
            var comp = new Component($"C3011-{s}", pidEdps[s - 1], quantity: 1, location: s, direction: 1);
            comp.AddDamageState(new DamageState(0, "No Damage"));
            comp.AddDamageState(new DamageState(1, "Minor Cracking"));
            comp.AddDamageState(new DamageState(2, "Significant Cracking"));
            asset.AddComponent(comp);
        }

        // Non-structural acceleration-sensitive (HVAC, equipment) — governed by PFA
        for (int s = 1; s <= 4; s++)
        {
            var comp = new Component($"D2021-{s}", pfaEdps[s - 1], quantity: 1, location: s, direction: 0);
            comp.AddDamageState(new DamageState(0, "No Damage"));
            comp.AddDamageState(new DamageState(1, "Functional", "Anchorage yields, repairable"));
            asset.AddComponent(comp);
        }

        // ─── Step 4: Demand Model ────────────────────────────────────────────
        var demandModel = new DemandModel();

        // PID lognormal marginals for stories 1–4 (median, β) at Sa=0.5g
        // Calibrated from NLTHA results (illustrative values)
        double[] pidMedians = [0.005, 0.007, 0.008, 0.006];   // radians
        double[] pidBetas   = [0.40, 0.38, 0.35, 0.40];

        for (int s = 0; s < 4; s++)
            demandModel.AddEdp(new EdpDistributionSpec(
                pidEdps[s],
                EdpDistributionKind.Lognormal,
                Math.Log(pidMedians[s]),
                pidBetas[s],
                TruncLower: 0.0));   // drift must be non-negative

        // PFA lognormal marginals (median in g, β)
        double[] pfaMedians = [0.25, 0.35, 0.45, 0.55];
        double[] pfaBetas   = [0.35, 0.35, 0.30, 0.30];

        for (int s = 0; s < 4; s++)
            demandModel.AddEdp(new EdpDistributionSpec(
                pfaEdps[s],
                EdpDistributionKind.Lognormal,
                Math.Log(pfaMedians[s]),
                pfaBetas[s],
                TruncLower: 0.0));

        // Correlation matrix for the 8 EDPs (PID1..4, PFA1..4)
        // Strong story-to-story correlation for same EDP type, moderate cross-type.
        double[,] rho = BuildEdpCorrelationMatrix();
        demandModel.SetCorrelation(rho);

        // ─── Step 5: Damage Model ────────────────────────────────────────────
        var damageModel = new DamageModel();

        // Structural frame fragility (B1033) – FEMA P-58 B1033.001a style
        double[][] structMedians = [[0.004, 0.008, 0.025], [0.004, 0.008, 0.025],
                                    [0.004, 0.008, 0.025], [0.004, 0.008, 0.025]];
        double structBeta = 0.40;

        for (int s = 0; s < 4; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"B1033-{s + 1}");
            var spec = new ComponentFragilitySpec(comp);
            foreach (double med in structMedians[s])
                spec.AddFragilityFunction(new FragilityFunction(med, structBeta));
            damageModel.Add(spec);
        }

        // Non-structural drift-sensitive (C3011)
        double[] nsdMedians = [0.004, 0.012];  // DS1, DS2
        double nsdBeta = 0.50;
        for (int s = 0; s < 4; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"C3011-{s + 1}");
            var spec = new ComponentFragilitySpec(comp);
            foreach (double med in nsdMedians)
                spec.AddFragilityFunction(new FragilityFunction(med, nsdBeta));
            damageModel.Add(spec);
        }

        // Non-structural acceleration-sensitive (D2021)
        double nsaMedian = 0.50;   // g – DS1 median
        double nsaBeta = 0.45;
        for (int s = 0; s < 4; s++)
        {
            var comp = asset.Components.First(c => c.Id == $"D2021-{s + 1}");
            var spec = new ComponentFragilitySpec(comp);
            spec.AddFragilityFunction(new FragilityFunction(nsaMedian, nsaBeta));
            damageModel.Add(spec);
        }

        // ─── Step 6: Loss Model ──────────────────────────────────────────────
        var lossModel = new LossModel();

        // Structural frame consequences (per unit = per frame bay)
        // DS1: $15k/bay, DS2: $80k/bay, DS3: $350k/bay
        double[] structCosts = [0, 15_000, 80_000, 350_000];
        double[] structTimes = [0, 5, 30, 120];  // days

        for (int s = 1; s <= 4; s++)
        {
            string id = $"B1033-{s}";
            lossModel.AddConsequences(id, [
                new ConsequenceFunction(1, DecisionVariable.Cost, structCosts[1], beta: 0.40),
                new ConsequenceFunction(2, DecisionVariable.Cost, structCosts[2], beta: 0.40),
                new ConsequenceFunction(3, DecisionVariable.Cost, structCosts[3], beta: 0.40),
                new ConsequenceFunction(1, DecisionVariable.Time, structTimes[1], beta: 0.30),
                new ConsequenceFunction(2, DecisionVariable.Time, structTimes[2], beta: 0.30),
                new ConsequenceFunction(3, DecisionVariable.Time, structTimes[3], beta: 0.30),
            ]);
        }

        // Non-structural drift-sensitive consequences
        double[] nsdCosts = [0, 5_000, 25_000];
        double[] nsdTimes = [0, 2, 10];

        for (int s = 1; s <= 4; s++)
        {
            string id = $"C3011-{s}";
            lossModel.AddConsequences(id, [
                new ConsequenceFunction(1, DecisionVariable.Cost, nsdCosts[1], beta: 0.50),
                new ConsequenceFunction(2, DecisionVariable.Cost, nsdCosts[2], beta: 0.50),
                new ConsequenceFunction(1, DecisionVariable.Time, nsdTimes[1], beta: 0.40),
                new ConsequenceFunction(2, DecisionVariable.Time, nsdTimes[2], beta: 0.40),
            ]);
        }

        // Non-structural acceleration-sensitive consequences
        for (int s = 1; s <= 4; s++)
        {
            string id = $"D2021-{s}";
            lossModel.AddConsequences(id, [
                new ConsequenceFunction(1, DecisionVariable.Cost, 8_000, beta: 0.45),
                new ConsequenceFunction(1, DecisionVariable.Time, 3, beta: 0.35),
            ]);
        }

        // ─── Step 7: Run Simulation ──────────────────────────────────────────
        var runner = new SimulationRunner(asset, demandModel, damageModel, lossModel)
        {
            Options = new SimulationOptions
            {
                SamplingMethod = SamplingMethod.LatinHypercube,
                DecisionVariables = DecisionVariable.Cost | DecisionVariable.Time,
            }
        };

        Console.WriteLine($"Running {N:N0} Monte Carlo simulations…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SimulationResult result = runner.Run(N, Seed);
        sw.Stop();
        Console.WriteLine($"Completed in {sw.ElapsedMilliseconds} ms.");
        Console.WriteLine();

        // ─── Step 8: Output Results ──────────────────────────────────────────
        result.PrintSummary("4-Story RC Office – Design Earthquake (Sa=0.5g)");
        Console.WriteLine();

        // Collapse proxy: P(DS3 for any structural frame)
        double pCollapse = 0.0;
        for (int s = 1; s <= 4; s++)
            pCollapse = Math.Max(pCollapse, result.GetExceedanceProbability($"B1033-{s}", 3));
        Console.WriteLine($"  P(Collapse proxy, any story DS3) : {pCollapse:P2}");
        Console.WriteLine();

        // Story damage state probabilities for the first structural frame
        Console.WriteLine("  Story 1 Structural Damage State Probabilities:");
        double[] probs = result.GetDamageStateProbabilities("B1033-1", 3);
        for (int k = 0; k < probs.Length; k++)
            Console.WriteLine($"    DS{k}: {probs[k]:P2}");

        Console.WriteLine();

        // Cost histogram
        Console.WriteLine("  Repair Cost Histogram:");
        var (edges, counts) = result.CostHistogram(bins: 10);
        for (int b = 0; b < counts.Length; b++)
        {
            double bar = counts[b] / (double)N;
            string barStr = new('█', (int)(bar * 40));
            Console.WriteLine($"    [{edges[b],10:N0} – {edges[b + 1],10:N0}] {barStr} {bar:P1}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static double[,] BuildEdpCorrelationMatrix()
    {
        // 8×8 correlation matrix: [PID1, PID2, PID3, PID4, PFA1, PFA2, PFA3, PFA4]
        // Strong within-type correlation (0.80–0.90), moderate cross-type (0.40)
        int n = 8;
        var rho = new double[n, n];

        // Diagonal = 1
        for (int i = 0; i < n; i++) rho[i, i] = 1.0;

        // Within PID (stories 1–4, indices 0–3)
        double[,] pidRho =
        {
            { 1.00, 0.85, 0.70, 0.55 },
            { 0.85, 1.00, 0.85, 0.70 },
            { 0.70, 0.85, 1.00, 0.85 },
            { 0.55, 0.70, 0.85, 1.00 },
        };

        // Within PFA (stories 1–4, indices 4–7)
        double[,] pfaRho =
        {
            { 1.00, 0.80, 0.65, 0.50 },
            { 0.80, 1.00, 0.80, 0.65 },
            { 0.65, 0.80, 1.00, 0.80 },
            { 0.50, 0.65, 0.80, 1.00 },
        };

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                rho[i, j] = pidRho[i, j];
                rho[i + 4, j + 4] = pfaRho[i, j];
            }
        }

        // Cross-type correlation PID↔PFA = 0.40 (moderate)
        for (int i = 0; i < 4; i++)
            for (int j = 4; j < 8; j++)
            {
                rho[i, j] = 0.40;
                rho[j, i] = 0.40;
            }

        return rho;
    }
}
