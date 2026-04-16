using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReSilico.UI.Services;
using System;
using System.Collections.ObjectModel;

namespace ReSilico.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } =
    [
        new("Dashboard", typeof(DashboardViewModel)),
        new("Input Configuration", typeof(InputConfigurationViewModel)),
        new("Correlation & Copulas", typeof(CorrelationMatrixViewModel)),
        new("Simulation Settings", typeof(SimulationSettingsViewModel)),
        new("Run Simulation", typeof(SimulationViewModel)),
        new("Results", typeof(ResultsViewModel)),
        new("Sensitivity Analysis", typeof(SensitivityAnalysisViewModel)),
        new("Data", typeof(DataViewModel)),
    ];

    public MainViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        Title = "ReSilico — Probabilistic Loss Assessment";
        _navigation.NavigationChanged += (_, vm) =>
        {
            CurrentPage = vm;
            SyncSelectedNavigationItem(vm.GetType());
        };

        foreach (var item in NavigationItems)
            item.NavigateRequested += (_, viewModelType) => _navigation.NavigateTo(viewModelType);
    }

    private void SyncSelectedNavigationItem(Type viewModelType)
    {
        foreach (var item in NavigationItems)
            item.IsSelected = item.ViewModelType == viewModelType;
    }
}

public sealed partial class NavigationItemViewModel(string label, Type viewModelType) : ObservableObject
{
    public string Label { get; } = label;
    public Type ViewModelType { get; } = viewModelType;

    [ObservableProperty]
    private bool _isSelected;

    public event EventHandler<Type>? NavigateRequested;

    [RelayCommand]
    private void Navigate() => NavigateRequested?.Invoke(this, ViewModelType);
}
