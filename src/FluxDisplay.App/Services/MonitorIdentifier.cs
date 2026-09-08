#pragma warning disable CS0219
using FluxDisplay.App.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace FluxDisplay.App.Services;

// Shows an identify overlay on every connected monitor: borderless topmost Window
// with AppWindow Presenter FullScreen, centered TextBlock number FontSize 300 Bold,
// closes after Task.Delay(durationSeconds*1000).
public sealed partial class MonitorIdentifier : IMonitorIdentifier
{
    public async Task IdentifyAsync(IReadOnlyList<DisplayInfo> displays, CancellationToken ct = default)
    {
        await IdentifyAsync(displays, 3, ct).ConfigureAwait(false);
    }

    public async Task IdentifyAsync(IReadOnlyList<DisplayInfo> displays, int durationSeconds, CancellationToken ct = default)
    {
        if (displays is null || displays.Count == 0)
        {
            return;
        }

        if (durationSeconds <= 0)
        {
            durationSeconds = 3;
        }

        var windows = new List<Window>();

        void CreateWindows()
        {
            int index = 1;
            foreach (var display in displays)
            {
                var window = new Window
                {
                    Title = $"Identify {index}",
                    Content = new Grid
                    {
                        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xCC, 0x08, 0x18, 0x27)),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = index.ToString(),
                                FontSize = 300,
                                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = new SolidColorBrush(Colors.White)
                            }
                        }
                    }
                };

                try
                {
                    var hwnd = WindowNative.GetWindowHandle(window);
                    var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);
                    if (appWindow is not null)
                    {
                        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                        try
                        {
                            var rect = new global::Windows.Graphics.RectInt32
                            {
                                X = display.Bounds.X,
                                Y = display.Bounds.Y,
                                Width = display.Bounds.Width,
                                Height = display.Bounds.Height
                            };
                            appWindow.MoveAndResize(rect);
                        }
                        catch
                        {
                        }

                        appWindow.IsShownInSwitchers = false;
                    }

                    _ = SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                }
                catch
                {
                }

                window.Activate();
                windows.Add(window);
                index++;
            }
        }

        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource();
            if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    CreateWindows();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                CreateWindows();
            }
            await tcs.Task.ConfigureAwait(false);
        }
        else
        {
            CreateWindows();
        }

        try
        {
            await Task.Delay(durationSeconds * 1000, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        void CloseWindows()
        {
            foreach (var w in windows)
            {
                try { w.Close(); } catch { }
            }
        }

        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(CloseWindows);
            await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            CloseWindows();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
