using ReSilico.UI.ViewModels;
using System;

namespace ReSilico.UI.Services;

public interface INavigationService
{
    ViewModelBase? CurrentView { get; }
    event EventHandler<ViewModelBase>? NavigationChanged;

    /// <summary>Register a singleton VM instance keyed by its type.</summary>
    void Register(ViewModelBase viewModel);

    /// <summary>Navigate to a previously registered VM of type <typeparamref name="T"/>.</summary>
    void NavigateTo<T>() where T : ViewModelBase;

    /// <summary>Navigate to a previously registered VM by runtime type.</summary>
    void NavigateTo(Type viewModelType);

    /// <summary>Navigate directly to a VM instance (does not require prior registration).</summary>
    void NavigateTo(ViewModelBase viewModel);
}
