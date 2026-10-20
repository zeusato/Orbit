using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Orbit.App;
using Orbit.App.Services;
using Orbit.App.UI;
using Orbit.Core;

internal static partial class Program
{
    private static void CheckHotkeyRules()
    {
        foreach (var hotkey in Enum.GetValues<MenuHotkey>())
        for (int flags = 0; flags < 64; flags++)
        {
            bool ctrl = (flags & 1) != 0, shift = (flags & 2) != 0, alt = (flags & 4) != 0,
                win = (flags & 8) != 0, down = (flags & 16) != 0, dragged = (flags & 32) != 0;
            var required = hotkey switch { MenuHotkey.CtrlSpace => 1, MenuHotkey.CtrlShiftSpace => 3, _ => 2 };
            var expected = flags == (required | 16 | 32);
            Assert(MenuHotkeys.ShouldActivate(hotkey, down, dragged, ctrl, shift, alt, win) == expected, $"exact modifiers and drag gate: {hotkey}/{flags}");
            Assert(MenuHotkeys.ShouldOpenQuickMenu(down, ctrl, shift, alt, win) == (!down && ctrl && !shift && !alt && !win),
                $"idle shortcut ignores drag preference, requires no mouse button: {hotkey}/{flags}");
        }
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"HotkeyNeedsShift\":true}")!;
        Assert(legacy.EffectiveHotkey == MenuHotkey.CtrlShiftSpace, "old Ctrl+Shift preference survives upgrade");
        Assert(new AppSettings().EffectiveHotkey == MenuHotkey.CtrlSpace, "existing default unchanged");
        legacy.SetHotkey(MenuHotkey.ShiftSpace);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy))!;
        Assert(restored.EffectiveHotkey == MenuHotkey.ShiftSpace && restored.HotkeyLabel == "Shift + Space", "Shift choice survives restart");
        restored.MenuHotkeyChoice = "unknown-future-choice";
        Assert(restored.EffectiveHotkey == MenuHotkey.CtrlSpace, "unknown choice uses safe legacy fallback");
    }

    private static void CheckHotkeySettingsUi(OrbitApplication app)
    {
        var settings = new SettingsWindow(app);
        try
        {
            settings.Show(); settings.UpdateLayout();
            var combo = Descendants<ComboBox>(settings).First();
            Assert(combo.Items.Cast<string>().SequenceEqual(new[] { "Ctrl + Space", "Ctrl + Shift + Space", "Shift + Space" }), "all three options displayed");
            combo.SelectedIndex = 2;
            Descendants<CheckBox>(settings).Single(b => b.Content.ToString()!.StartsWith("Tự tải")).IsChecked = false;
            Descendants<Button>(settings).Single(b => Equals(b.Content, "Lưu cài đặt")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!settings.IsVisible && app.Settings.EffectiveHotkey == MenuHotkey.ShiftSpace, "save applies immediately");
            Assert(AppSettings.Load().EffectiveHotkey == MenuHotkey.ShiftSpace, "choice saved to disk");
            Assert(!app.Settings.AutoUpdate && !AppSettings.Load().AutoUpdate, "auto-update opt-out persists");
            Assert(Descendants<TextBlock>(app.MainWindow).Any(t => t.Text.Contains("bấm Shift + Space")), "home instruction updated");
        }
        finally { if (settings.IsVisible) settings.Close(); }
        var reopened = new SettingsWindow(app);
        try
        {
            reopened.Show(); reopened.UpdateLayout();
            var combo = Descendants<ComboBox>(reopened).First();
            Assert(combo.SelectedIndex == 2, "reopened settings retains selection");
            combo.SelectedIndex = 0;
            Descendants<CheckBox>(reopened).Single(b => b.Content.ToString()!.StartsWith("Tự tải")).IsChecked = true;
            Descendants<Button>(reopened).Single(b => Equals(b.Content, "Hủy")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(app.Settings.EffectiveHotkey == MenuHotkey.ShiftSpace && AppSettings.Load().EffectiveHotkey == MenuHotkey.ShiftSpace, "cancel does not change selected shortcut");
            Assert(!app.Settings.AutoUpdate, "cancel preserves auto-update preference");
        }
        finally { if (reopened.IsVisible) reopened.Close(); }
    }
}
