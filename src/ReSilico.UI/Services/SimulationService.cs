using Microsoft.Extensions.Logging;
using ReSilico.Analysis.Simulation;
using ReSilico.UI.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public class SimulationService : ISimulationService
{
    private readonly ILogger<SimulationService> _logger;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public event EventHandler<double>? ProgressChanged;

    public SimulationService(ILogger<SimulationService> logger)
    {
        _logger = logger;
    }

    public async Task<SimulationResult> RunAsync(SimulationConfiguration config, CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        ProgressChanged?.Invoke(this, 0.0);

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Starting simulation: {N} realizations, seed={Seed}",
                    config.NumberOfRealizations, config.RandomSeed);

                ProgressChanged?.Invoke(this, 0.1);
                cancellationToken.ThrowIfCancellationRequested();

                var pipeline = SimulationModelFactory.BuildPipeline(config);
                var result = config.UseAdaptive
                    ? pipeline.RunAdaptive(SimulationModelFactory.BuildAdaptiveOptions(config))
                    : pipeline.Run(config.NumberOfRealizations, config.RandomSeed);

                ProgressChanged?.Invoke(this, 1.0);
                _logger.LogInformation("Simulation complete. Mean cost = {Cost:N0}", result.MeanCost);
                return result;

            }, cancellationToken);
        }
        finally
        {
            _isRunning = false;
        }
    }
}
