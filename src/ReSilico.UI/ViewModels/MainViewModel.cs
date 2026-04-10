using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.UI.Services;

namespace ReSilico.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public MainViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        Title = "ReSilico — Probabilistic Loss Assessment";
        _navigation.NavigationChanged += (_, vm) => CurrentPage = vm;
    }

    [RelayCommand]
    private void GoToDashboard() => _navigation.NavigateTo<DashboardViewModel>();

    [RelayCommand]
    private void GoToDistributionEditor() => _navigation.NavigateTo<DistributionEditorViewModel>();

    [RelayCommand]
    private void GoToCorrelationMatrix() => _navigation.NavigateTo<CorrelationMatrixViewModel>();

    [RelayCommand]
    private void GoToSimulation() => _navigation.NavigateTo<SimulationViewModel>();

    [RelayCommand]
    private void GoToResults() => _navigation.NavigateTo<ResultsViewModel>();
}
