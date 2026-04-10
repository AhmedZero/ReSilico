using Microsoft.Extensions.Logging;
using ReSilico.UI.Models;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public class DataService(ILogger<DataService> logger) : IDataService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task<SimulationConfiguration> LoadConfigurationAsync(string filePath)
    {
        logger.LogInformation("Loading configuration from {Path}", filePath);
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<SimulationConfiguration>(stream, _jsonOptions)
               ?? new SimulationConfiguration();
    }

    public async Task SaveConfigurationAsync(SimulationConfiguration config, string filePath)
    {
        logger.LogInformation("Saving configuration to {Path}", filePath);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, config, _jsonOptions);
    }
}
