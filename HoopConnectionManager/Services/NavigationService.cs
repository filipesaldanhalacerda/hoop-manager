using HoopConnectionManager.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de navegação MVVM.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Stack<object> _history = new();

    public object? CurrentViewModel { get; private set; }

    public event EventHandler<NavigationEventArgs>? Navigated;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo<T>() where T : class
    {
        var viewModel = _serviceProvider.GetRequiredService<T>();
        NavigateTo(viewModel);
    }

    public void NavigateTo(object viewModel)
    {
        if (CurrentViewModel is not null)
        {
            _history.Push(CurrentViewModel);
        }

        CurrentViewModel = viewModel;
        Navigated?.Invoke(this, new NavigationEventArgs(CurrentViewModel));
    }

    public void GoBack()
    {
        if (_history.Count == 0)
        {
            return;
        }

        CurrentViewModel = _history.Pop();
        Navigated?.Invoke(this, new NavigationEventArgs(CurrentViewModel));
    }
}
