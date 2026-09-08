using Microsoft.Win32;

namespace FluxDisplay.App.Services;

// Manages Windows startup registry entry at HKCU\Software\Microsoft\Windows\CurrentVersion\Run
// Value name: FluxDisplay, value: Environment.ProcessPath (quoted).
public sealed class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FluxDisplay";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (key is null)
                {
                    return false;
                }

                var value = key.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    private static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                // Fallback to current process main module.
                exePath = Environment.GetCommandLineArgs().FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            // Quote path to handle spaces.
            var value = $"\"{exePath}\"";

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key is null)
            {
                return;
            }

            key.SetValue(ValueName, value, RegistryValueKind.String);
        }
        catch
        {
            // Registry access may fail without permission; swallow.
        }
    }

    private static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
        }
    }
}
