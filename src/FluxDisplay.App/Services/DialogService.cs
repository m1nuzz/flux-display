using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxDisplay.App.Services;

// Shows modal dialogs on top of the main window via ContentDialog.
public sealed class DialogService : IDialogService
{
    public async Task ShowInfoAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message, "OK", null);
        await ShowAsync(dialog).ConfigureAwait(false);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string primaryButtonText = "Apply", string closeButtonText = "Cancel")
    {
        var dialog = CreateDialog(title, message, primaryButtonText, closeButtonText);
        var result = await ShowAsync(dialog).ConfigureAwait(false);
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message, "OK", null);
        await ShowAsync(dialog).ConfigureAwait(false);
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
