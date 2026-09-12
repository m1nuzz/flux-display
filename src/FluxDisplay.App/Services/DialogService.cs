using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxDisplay.App.Services;

// Shows modal dialogs on top of the main window via ContentDialog.
public sealed class DialogService : IDialogService
{
    public async Task ShowInfoAsync(string title, string message)
    {
        await RunOnUIAsync(async () =>
        {
            var dialog = CreateDialog(title, message, "OK", null);
            return await ShowAsync(dialog).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string primaryButtonText = "Apply", string closeButtonText = "Cancel")
    {
        var result = await RunOnUIAsync(async () =>
        {
            var dialog = CreateDialog(title, message, primaryButtonText, closeButtonText);
            return await ShowAsync(dialog).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        await RunOnUIAsync(async () =>
        {
            var dialog = CreateDialog(title, message, "OK", null);
            return await ShowAsync(dialog).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    // WinUI controls (including ContentDialog construction) require the UI
    // thread. Callers come from background threads (update orchestrator), so
    // the whole create+show runs marshalled. Returns default on failure
    // instead of throwing into background flows.
    private static async Task<T> RunOnUIAsync<T>(Func<Task<T>> work)
    {
        var window = App.MainWindow;
        if (window is null)
        {
            return default!;
        }

        if (window.DispatcherQueue.HasThreadAccess)
        {
            try
            {
                return await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Helpers.AppLog.Error(ex, "DialogService.RunOnUI");
                return default!;
            }
        }

        var tcs = new TaskCompletionSource<T>();
        if (!window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                tcs.SetResult(await work().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Helpers.AppLog.Error(ex, "DialogService.RunOnUI");
                tcs.SetResult(default!);
            }
        }))
        {
            return default!;
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    private static ContentDialog CreateDialog(string title, string message, string? primaryText, string? closeText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText ?? (primaryText is null ? null : string.Empty),
            DefaultButton = ContentDialogButton.Primary
        };

        try
        {
            var window = App.MainWindow;
            if (window?.Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }
        }
        catch
        {
        }

        return dialog;
    }

    private static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        Helpers.AppLog.Info($"DialogService.ShowAsync title='{dialog.Title}' hasThreadAccess check");
        var window = App.MainWindow;
        if (window is null)
        {
            return ContentDialogResult.None;
        }

        if (!window.DispatcherQueue.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource<ContentDialogResult>();
            window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (dialog.XamlRoot is null && window.Content is FrameworkElement root)
                    {
                        dialog.XamlRoot = root.XamlRoot;
                    }

                    var r = await dialog.ShowAsync();
                    tcs.SetResult(r);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return await tcs.Task.ConfigureAwait(false);
        }
        else
        {
            if (dialog.XamlRoot is null && window.Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            return await dialog.ShowAsync();
        }
    }
}
