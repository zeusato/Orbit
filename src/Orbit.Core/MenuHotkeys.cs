namespace Orbit.Core;

public enum MenuHotkey { CtrlSpace, CtrlShiftSpace, ShiftSpace }

public static class MenuHotkeys
{
    // The utility launcher is fixed Ctrl+Space. Drag-only alternatives (especially
    // Shift+Space) must not become global shortcuts during normal typing.
    public static bool ShouldOpenQuickMenu(bool mouseButtonDown, bool control, bool shift, bool alt, bool windows) =>
        !mouseButtonDown && control && !shift && !alt && !windows;

    public static string Label(MenuHotkey hotkey) => hotkey switch
    {
        MenuHotkey.ShiftSpace => "Shift + Space",
        MenuHotkey.CtrlShiftSpace => "Ctrl + Shift + Space",
        _ => "Ctrl + Space"
    };

    // Called only for Space down. Modifiers must match exactly; ordinary typing
    // and other shortcuts keep passing through to Windows/the focused app.
    public static bool ShouldActivate(MenuHotkey hotkey, bool mouseDown, bool dragged,
        bool control, bool shift, bool alt, bool windows)
    {
        if (!mouseDown || !dragged || alt || windows) return false;
        return hotkey switch
        {
            MenuHotkey.CtrlSpace => control && !shift,
            MenuHotkey.CtrlShiftSpace => control && shift,
            MenuHotkey.ShiftSpace => !control && shift,
            _ => false
        };
    }
}
