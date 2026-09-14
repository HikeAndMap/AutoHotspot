using Microsoft.Win32;

namespace AutoHotspot;

/// <summary>
/// The per-user "Run" registry value that starts AutoHotspot at sign-in. Its presence is the
/// enabled/disabled setting itself, so there is no separate config file to keep in sync.
/// </summary>
internal static class AutostartRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoHotspot";

    public static string ExpectedCommand => $"\"{Environment.ProcessPath}\" {Program.StartupArgument}";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Enable()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// When enabled but pointing at a different exe path (the exe was moved), rewrites it to this exe.
    /// Returns true when the value was changed.
    /// </summary>
    public static bool RepairPathIfEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is not string current)
            return false;
        if (string.Equals(current, ExpectedCommand, StringComparison.OrdinalIgnoreCase))
            return false;

        key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
        return true;
    }
}
