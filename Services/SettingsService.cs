using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace PersianKeyboardConverter.Services
{
    /// <summary>
    /// Manages application settings with JSON persistence.
    /// </summary>
    public class AppSettings
    {
        public string HotkeyKey { get; set; } = "F10";
        public uint HotkeyModifiers { get; set; } = HotkeyManager.MOD_NONE | HotkeyManager.MOD_NOREPEAT;
        public string CorrectionHotkeyKey { get; set; } = "F9";
        public uint CorrectionHotkeyModifiers { get; set; } = HotkeyManager.MOD_NONE | HotkeyManager.MOD_NOREPEAT;
        public string TranslationHotkeyKey { get; set; } = "F8";
        public uint TranslationHotkeyModifiers { get; set; } = HotkeyManager.MOD_NONE | HotkeyManager.MOD_NOREPEAT;
        public bool ConversionEnabled { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
    }

    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersianKeyboardConverter",
            "settings.json");

        private static readonly string AppName = "PersianKeyboardConverter";
        private static readonly string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static AppSettings Current { get; private set; } = new AppSettings();

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) Current = loaded;
                }
            }
            catch
            {
                Current = new AppSettings();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                ApplyAutostart(Current.StartWithWindows);
            }
            catch
            {
                // Silently ignore persistence errors
            }
        }

        public static Keys GetHotkeyKey()
        {
            if (Enum.TryParse<Keys>(Current.HotkeyKey, out Keys k))
                return k;
            return Keys.F10;
        }

        public static void SetHotkeyKey(Keys key)
        {
            Current.HotkeyKey = key.ToString();
        }

        public static Keys GetCorrectionHotkeyKey()
        {
            if (Enum.TryParse<Keys>(Current.CorrectionHotkeyKey, out Keys k))
                return k;
            return Keys.F9;
        }

        public static void SetCorrectionHotkeyKey(Keys key)
        {
            Current.CorrectionHotkeyKey = key.ToString();
        }

        public static Keys GetTranslationHotkeyKey()
        {
            if (Enum.TryParse<Keys>(Current.TranslationHotkeyKey, out Keys k))
                return k;
            return Keys.F8;
        }

        public static void SetTranslationHotkeyKey(Keys key)
        {
            Current.TranslationHotkeyKey = key.ToString();
        }

        private static void ApplyAutostart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
                if (key == null) return;

                if (enable)
                {
                    string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            catch { }
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }
    }
}
