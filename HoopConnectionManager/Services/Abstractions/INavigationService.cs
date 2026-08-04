namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Argumento do evento de navegação entre ViewModels.
/// </summary>
public sealed class NavigationEventArgs : EventArgs
{
    public object? ViewModel { get; }

    public NavigationEventArgs(object? viewModel)
    {
        ViewModel = viewModel;
    }
}

/// <summary>
/// Serviço de navegação desacoplado para troca de telas no padrão MVVM.
/// </summary>
public interface INavigationService
{
    object? CurrentViewModel { get; }
    event EventHandler<NavigationEventArgs>? Navigated;
    void NavigateTo<T>() where T : class;
    void NavigateTo(object viewModel);
    void GoBack();
}
