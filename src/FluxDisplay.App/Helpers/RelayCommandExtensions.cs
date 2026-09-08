using CommunityToolkit.Mvvm.Input;

namespace FluxDisplay.App.Helpers;

public static class RelayCommandExtensions
{
    public static bool TryExecute(this IRelayCommand command, object? parameter = null)
    {
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    public static async Task<bool> TryExecuteAsync(this IAsyncRelayCommand command, object? parameter = null)
    {
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        await command.ExecuteAsync(parameter).ConfigureAwait(true);
        return true;
    }
}
