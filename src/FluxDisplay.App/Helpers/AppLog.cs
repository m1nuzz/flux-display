using System.Diagnostics;
using System.Text;

namespace FluxDisplay.App.Helpers;

// Crash-diagnosis file logger. Append-only, flushed per write, so the last
// lines survive even a native "Unknown Hard Error" crash.
// Location: %LOCALAPPDATA%\FluxDisplay\logs\flux-YYYYMMDD.log
// Override with FLUX_LOG_DIR env var.
public static class AppLog
{
    private static readonly object _gate = new();
    private static string? _path;
    private static bool _headerWritten;

    public static string LogPath
    {
        get
        {
            if (_path is not null)
            {
                return _path;
            }

            var dir = Environment.GetEnvironmentVariable("FLUX_LOG_DIR");
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FluxDisplay", "logs");
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch
            {
                dir = Path.GetTempPath();
            }

            _path = Path.Combine(dir, $"flux-{DateTime.Now:yyyyMMdd}.log");
            return _path;
        }
    }

    public static void RunHeader(string[] args)
    {
        if (_headerWritten)
        {
            return;
        }

        _headerWritten = true;
        var sb = new StringBuilder();
        sb.AppendLine($"===== run pid={Environment.ProcessId} {DateTime.Now:O} =====");
        sb.AppendLine($"exe={Environment.ProcessPath}");
        sb.AppendLine($"os={Environment.OSVersion} x64={Environment.Is64BitProcess}");
        sb.AppendLine($"args={string.Join(" ", args)}");
        sb.AppendLine($"log={LogPath}");
        WriteRaw(sb.ToString());
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(Exception ex, string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{context}: {ex.GetType().Name}: {ex.Message}");
        sb.AppendLine(ex.StackTrace);
        if (ex.InnerException is not null)
        {
            sb.AppendLine($"inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            sb.AppendLine(ex.InnerException.StackTrace);
        }

        Write("ERROR", sb.ToString().TrimEnd());
    }

    // Logs method entry/exit with elapsed time. Usage:
    //   using var _ = AppLog.Scope("DisplayService.ChangeDisplayModeAsync");
    public static IDisposable Scope(string name, string? detail = null) => new LogScope(name, detail);

    public static void PInvoke(string api, string detail) => Write("PINVOKE", $"{api} {detail}");

    // Native "Unknown Hard Error" can't be caught in managed code. A WER local
    // dump gives the faulting module + stack. Logs whether dumps are on, plus
    // the one-liner to enable them.
    public static void LogDumpHint()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\FluxDisplay.App.exe");
            if (key?.GetValue("DumpFolder") is string folder && Directory.Exists(folder))
            {
                Write("INFO", $"WER dumps ON -> {folder}");
                return;
            }
        }
        catch
        {
        }

        Write("WARN", "WER dumps OFF. To capture a .dmp on crash, run (admin not required): "
            + @"reg add ""HKCU\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\FluxDisplay.App.exe"" "
            + @"/v DumpFolder /t REG_EXPAND_SZ /d ""%LOCALAPPDATA%\FluxDisplay\dumps"" /f "
            + @"&& reg add ""HKCU\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\FluxDisplay.App.exe"" "
            + @"/v DumpType /t REG_DWORD /d 2 /f");
    }

    private static void Write(string level, string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var tid = Environment.CurrentManagedThreadId;
        foreach (var line in message.Split('\n'))
        {
            WriteRaw($"{stamp} [{level}] [t{tid}] {line.TrimEnd('\r')}");
        }
    }

    private static void WriteRaw(string text)
    {
        lock (_gate)
        {
            try
            {
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
            catch
            {
                // Logger must never crash the app. Debug output as last resort.
                try
                {
                    Debug.WriteLine(text);
                }
                catch
                {
                }
            }
        }
    }

    private sealed class LogScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _sw;
        private bool _disposed;

        public LogScope(string name, string? detail)
        {
            _name = name;
            _sw = Stopwatch.StartNew();
            Write("ENTER", detail is null ? name : $"{name} {detail}");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sw.Stop();
            Write("EXIT", $"{_name} {_sw.ElapsedMilliseconds}ms");
        }
    }
}
