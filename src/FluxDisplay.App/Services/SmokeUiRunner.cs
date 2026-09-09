using FluxDisplay.App.Helpers;
using FluxDisplay.App.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FluxDisplay.App.Services;

internal static class SmokeUiRunner
{
    public static async Task RunAsync(MainWindow window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var log = new List<string> { $"Smoke UI {DateTime.Now:O}", $"Out={outputDir}" };
        try
        {
            SizeWindow(window, 1280, 800);
            window.Activate();
            await Task.Delay(600);

            var services = App.Services;
            var presetPath = services.Presets.StoragePath;
            string? backup = File.Exists(presetPath) ? await File.ReadAllTextAsync(presetPath) : null;
            try
            {
                window.NavigateTo("presets");
                await window.ViewModel.Presets.ReloadAsync();
                await WaitAsync();
                await Shot(window, outputDir, "01-presets-empty.png", log);

                await SeedPresetsAsync(services);
                await window.ViewModel.Presets.ReloadAsync();
                await WaitAsync();
                await Shot(window, outputDir, "02-presets-filled.png", log);

                window.NavigateTo("settings");
                await window.ViewModel.SettingsViewModel.LoadAsync();
                await WaitAsync();
                await Shot(window, outputDir, "03-settings.png", log);

                window.NavigateTo("create");
                await window.ViewModel.Create.ResetAsync();
                await WaitAsync();
                await Shot(window, outputDir, "04-create-step0-monitors.png", log);

                var monitor = window.ViewModel.Create.Monitors.FirstOrDefault();
                if (monitor is not null)
                {
                    window.ViewModel.Create.SelectMonitor(monitor);
                    await window.ViewModel.Create.NextAsync();
                    await WaitAsync();
                    await Shot(window, outputDir, "05-create-step1-mode.png", log);

                    await window.ViewModel.Create.NextAsync();
                    await WaitAsync();
                    await Shot(window, outputDir, "06-create-step2-name.png", log);
                }
                else
                {
                    log.Add("create wizard: no monitors");
                }

                if (window.Content is FrameworkElement root && root.XamlRoot is not null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Apply preset",
                        Content = new TextBlock { Text = "Apply 'Gaming 240Hz'?", TextWrapping = TextWrapping.Wrap },
                        PrimaryButtonText = "Apply",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = root.XamlRoot
                    };
                    var shown = dialog.ShowAsync().AsTask();
                    await Task.Delay(500);
                    await Shot(window, outputDir, "07-dialog-confirm.png", log);
                    dialog.Hide();
                    try { await shown; } catch { }
                }

                var displays = await services.Display.GetDisplaysAsync();
                if (displays.Count > 0)
                {
                    var identify = services.Identifier.IdentifyAsync(displays, 4);
                    await Task.Delay(900);
                    await WindowCapture.CaptureProcessWindowsAsync(outputDir, "08-identify");
                    log.Add($"identify windows captured displays={displays.Count}");
                    try { await identify; } catch { }
                }

                window.NavigateTo("presets");
                await WaitAsync();
                if (services.Tray is TrayService tray && tray.TryShowContextFlyout())
                {
                    await Task.Delay(700);
                    await WindowCapture.CaptureProcessWindowsAsync(outputDir, "09-tray");
                    await Shot(window, outputDir, "09-tray-main.png", log);
                    log.Add("tray flyout shown");
                }
                else
                {
                    log.Add("tray flyout not shown");
                }
            }
            finally
            {
                RestorePresets(presetPath, backup);
            }
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            await File.WriteAllTextAsync(Path.Combine(outputDir, "error.txt"), ex.ToString());
        }

        var pngs = Directory.GetFiles(outputDir, "*.png").Select(Path.GetFileName).OrderBy(x => x);
        log.Add($"png={pngs.Count()}");
        log.AddRange(pngs.Select(name => $" - {name}"));
        await File.WriteAllTextAsync(Path.Combine(outputDir, "index.txt"), string.Join(Environment.NewLine, log));
    }

    private static async Task Shot(MainWindow window, string dir, string name, List<string> log)
    {
        var path = Path.Combine(dir, name);
        await WindowCapture.CaptureMainAsync(window, path);
        log.Add(File.Exists(path) ? $"ok {name} {new FileInfo(path).Length}" : $"missing {name}");
    }

    private static Task WaitAsync() => Task.Delay(450);

    private static void SizeWindow(MainWindow window, int width, int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow?.MoveAndResize(new Windows.Graphics.RectInt32(40, 40, width, height));
        }
        catch
        {
        }
    }

    private static async Task SeedPresetsAsync(AppServices services)
    {
        var displays = await services.Display.GetDisplaysAsync();
        var display = displays.FirstOrDefault();
        var path = display?.DevicePath ?? @"\\.\DISPLAY1";
        var name = display?.FriendlyName ?? "Generic PnP Monitor";
        var collection = new PresetCollection
        {
            Presets =
            [
                new Preset
                {
                    Name = "Gaming 240Hz",
                    DevicePath = path,
                    FriendlyMonitorName = name,
                    Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 240, BitsPerPel = 32 },
                    ScalePercent = 100
                },
                new Preset
                {
                    Name = "Work 60Hz",
                    DevicePath = path,
                    FriendlyMonitorName = name,
                    Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 60, BitsPerPel = 32 },
                    ScalePercent = 100
                }
            ]
        };
        await services.Presets.SaveAsync(collection);
    }

    private static void RestorePresets(string path, string? backup)
    {
        try
        {
            if (backup is null)
            {
                if (File.Exists(path))
                {
                    File.WriteAllText(path, "{\"version\":1,\"presets\":[]}");
                }
            }
            else
            {
                File.WriteAllText(path, backup);
            }
        }
        catch
        {
        }
    }
}
