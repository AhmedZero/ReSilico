using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using ReSilico.UI.Services;
using ReSilico.UI.ViewModels;
using ReSilico.UI.Views;

namespace ReSilico.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ── Services ──────────────────────────────────────────────────────
            var loggerFactory = LoggerFactory.Create(b =>
                b.AddConsole().SetMinimumLevel(LogLevel.Debug));

            ISimulationService simulationService =
                new SimulationService(loggerFactory.CreateLogger<SimulationService>());
            IDataService dataService =
                new DataService(loggerFactory.CreateLogger<DataService>());

            // File dialog service — lazily captures the main window reference.
            MainWindow? mainWindow = null;
            IFileDialogService fileDialogs = new FileDialogService(() => mainWindow);

            // ── Navigation ────────────────────────────────────────────────────
            var navigation = new NavigationService();

            // ── ViewModels ────────────────────────────────────────────────────
            var resultsVm    = new ResultsViewModel(fileDialogs);
            var distEditorVm = new DistributionEditorViewModel(fileDialogs);
            var corrMatrixVm = new CorrelationMatrixViewModel();
            var simVm        = new SimulationViewModel(simulationService, navigation, resultsVm);
            var dashVm       = new DashboardViewModel(
                navigation, simulationService, fileDialogs, distEditorVm, resultsVm);

            // Register so NavigateTo<T>() works from any VM
            navigation.Register(dashVm);
            navigation.Register(distEditorVm);
            navigation.Register(corrMatrixVm);
            navigation.Register(simVm);
            navigation.Register(resultsVm);

            var mainVm = new MainViewModel(navigation);

            mainWindow = new MainWindow { DataContext = mainVm };
            desktop.MainWindow = mainWindow;

            // Navigate to dashboard on startup
            navigation.NavigateTo<DashboardViewModel>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
