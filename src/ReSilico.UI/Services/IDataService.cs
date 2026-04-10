using ReSilico.UI.Models;
using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public interface IDataService
{
    Task<SimulationConfiguration> LoadConfigurationAsync(string filePath);
    Task SaveConfigurationAsync(SimulationConfiguration config, string filePath);
}
