using ReSilico.UI.ViewModels;
using System;
using System.Collections.Generic;

namespace ReSilico.UI.Services;

public class NavigationService : INavigationService
{
    private readonly Dictionary<Type, ViewModelBase> _registry = [];
    private ViewModelBase? _currentView;

    public ViewModelBase? CurrentView => _currentView;
    public event EventHandler<ViewModelBase>? NavigationChanged;

    public void Register(ViewModelBase viewModel)
        => _registry[viewModel.GetType()] = viewModel;

    public void NavigateTo<T>() where T : ViewModelBase
    {
        if (_registry.TryGetValue(typeof(T), out var vm))
            NavigateTo(vm);
    }

    public void NavigateTo(ViewModelBase viewModel)
    {
        _currentView = viewModel;
        NavigationChanged?.Invoke(this, viewModel);
    }
}
