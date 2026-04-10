using ReSilico.Analysis.Simulation;
using ReSilico.UI.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public interface ISimulationService
{
    bool IsRunning { get; }
    event EventHandler<double>? ProgressChanged;
    Task<SimulationResult> RunAsync(SimulationConfiguration config, CancellationToken cancellationToken = default);
}
