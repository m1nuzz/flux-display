using Microsoft.Win32;

namespace FluxDisplay.App.Helpers;

public static class RegistryHelper
{
    public static string? GetCurrentUserValue(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool SetCurrentUserValue(string subKey, string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKey, true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(name, value, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DeleteCurrentUserValue(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
