using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Orbit.App.Services;

public sealed class AppSettings
{
    public bool HotkeyNeedsShift { get; set; }
    public string? MenuHotkeyChoice { get; set; }
    [JsonIgnore]
    public MenuHotkey EffectiveHotkey => MenuHotkeyChoice switch
    {
        nameof(MenuHotkey.ShiftSpace) => MenuHotkey.ShiftSpace,
        nameof(MenuHotkey.CtrlShiftSpace) => MenuHotkey.CtrlShiftSpace,
        nameof(MenuHotkey.CtrlSpace) => MenuHotkey.CtrlSpace,
        _ => HotkeyNeedsShift ? MenuHotkey.CtrlShiftSpace : MenuHotkey.CtrlSpace
    };
    public void SetHotkey(MenuHotkey hotkey)
    {
        if (!Enum.IsDefined(hotkey)) throw new ArgumentOutOfRangeException(nameof(hotkey));
        MenuHotkeyChoice = hotkey.ToString();
        HotkeyNeedsShift = hotkey == MenuHotkey.CtrlShiftSpace; // Compatibility with pre-0.2.2 settings.
    }
    public bool Glass { get; set; } = true;
    public string Theme { get; set; } = "System";
    public bool StartupPromptCompleted { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; }
    public bool ShouldOfferStartup(bool backgroundLaunch, bool alreadyEnabled) => !StartupPromptCompleted && !backgroundLaunch && !alreadyEnabled;

    public void CompleteStartupPrompt(Action? enableStartup = null)
    {
        enableStartup?.Invoke();
        var previous = StartupPromptCompleted;
        StartupPromptCompleted = true;
        try { Save(); }
        catch { StartupPromptCompleted = previous; throw; }
    }
    [JsonIgnore]
    public string HotkeyLabel => MenuHotkeys.Label(EffectiveHotkey);
    public static string DataDirectory { get; } = Environment.GetEnvironmentVariable("ORBIT_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit");
    private static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try { return File.Exists(SettingsFile) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var temporary = SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsFile, true);
    }
    public bool IsDark
    {
        get
        {
            if (Theme is "Dark" or "Light") return Theme == "Dark";
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int mode && mode == 0;
        }
    }
    public static bool StartupEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("Orbit") != null; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("Orbit", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("Orbit", false);
        }
    }
}
