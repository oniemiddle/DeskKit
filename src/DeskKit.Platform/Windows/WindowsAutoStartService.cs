using System.Security;
using Microsoft.Win32;

namespace DeskKit.Platform.Windows;

/// <summary>
/// Registers the application under the per-user Run key, which is the least
/// invasive way to start with Windows: it needs no elevated rights, no
/// scheduled task, and the user can remove it from Task Manager's start-up tab.
/// </summary>
public sealed class WindowsAutoStartService(string? valueName = null, string? executablePath = null) : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName = valueName ?? "DeskKit";
    private readonly string _executablePath = executablePath
                                              ?? Environment.ProcessPath
                                              ?? throw new InvalidOperationException("The process path is not available.");

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(_valueName) is string { Length: > 0 };
            }
            catch (SecurityException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            // Quoted so a path containing spaces is parsed as one argument.
            key.SetValue(_valueName, $"\"{_executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
