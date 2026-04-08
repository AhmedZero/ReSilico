# ReSilico

**ReSilico** is a high-performance probabilistic damage and loss assessment engine written in C#.

It enables uncertainty-aware evaluation of structural performance under hazards using stochastic simulation, fragility modeling, and loss estimation.

---

## 🚀 Features

- 🎲 Monte Carlo & Latin Hypercube Sampling (LHS)
- 🔗 Correlated random variables (Gaussian copula)
- 📉 Fragility-based damage assessment
- 💰 Loss estimation (repair cost, repair time)
- 📊 Full statistical outputs:
  - Mean, Std Dev, CoV
  - Percentiles (5%, 50%, 95%)
  - Exceedance probabilities
- ⚡ High performance (10,000 simulations in ~100–500 ms)
- 🧱 Clean layered architecture
- 🧪 45+ unit tests for numerical validation

---

## 🧠 Core Concept

ReSilico performs probabilistic analysis using the following pipeline:

EDP → Demand Model → Damage Model → Loss Model → Statistics

Where:

- **EDP**: Engineering Demand Parameters (e.g., Sa, drift)
- **Damage Model**: Uses fragility functions
- **Loss Model**: Maps damage → cost/time

---

## 🏗️ Architecture

ReSilico
├── Core        # Distributions, sampling, random variables
├── Domain      # EDP, components, assets, damage states
├── Analysis    # Demand, damage, loss models
├── Examples    # End-to-end workflows
└── Tests       # Unit tests

---

## ⚙️ Installation

```bash
git clone https://github.com/AhmedZero/ReSilico.git
cd ReSilico
dotnet build
```

---

## ▶️ Example Usage

### Basic Workflow

```csharp
var runner = new SimulationRunner(...);
var result = runner.Run(10000);

Console.WriteLine($"Mean Loss: {result.MeanLoss}");
```

---

## 📊 Example Output

### 4-Story RC Office (Sa = 0.5g)

Simulations: 10,000  
Completed in 452 ms  

Mean Repair Cost: 350,503  
Std Dev: 279,084  
CoV: 79.6%  
Median: 296,396  
95th Percentile: 813,961  

P(Collapse proxy): 1.49%  

---

### 6-Story Steel Moment Frame (Sa = 0.35g)

Simulations: 10,000  
Completed in 119 ms  

Mean Repair Cost: 412,498  
Median: 370,644  
95th Percentile: 895,426  

P(Cost > $1M): 3.56%  
P(Collapse proxy): 0.03%  

---

## 📈 Key Capabilities

### 🔹 Fragility Functions

P(DS ≥ ds | EDP) = Φ((ln(EDP) − ln(θ)) / β)

---

### 🔹 Correlation Modeling

Gaussian copula:

1. Generate independent samples  
2. Apply inverse normal transform  
3. Apply correlation (Cholesky)  
4. Transform back  

---

### 🔹 Sampling Methods

- Monte Carlo
- Latin Hypercube Sampling (LHS)

---

## ⚡ Performance

| Simulations | Time |
|------------|------|
| 10,000     | ~85–450 ms |

Optimized using:
- Efficient memory usage
- Vectorized operations
- Parallel execution

---

## 🧪 Testing

```bash
dotnet test
```

- 45+ unit tests
- Distribution validation
- Sampling correctness
- Fragility evaluation

---

## 📦 Dependencies

- USACE-RMC Numerics (math & statistics)

---

## 🛠️ Roadmap

- [ ] Advanced copulas (t-copula)
- [ ] Adaptive Monte Carlo
- [ ] Sensitivity analysis
- [ ] UI (desktop application)
- [ ] GPU acceleration

---

## 🤝 Contributing

Contributions are welcome.

- Open issues
- Submit pull requests
- Suggest features

---

## 📄 License

MIT License

---

## 👨‍💻 Author

Ahmed Elsayed Helal Mohammed  
Zagazig University  

---

## ⭐ Support

If you find this project useful, consider giving it a star ⭐
