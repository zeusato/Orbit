using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Orbit.App;
using Orbit.App.Services;
using Orbit.App.UI;

internal static partial class Program
{
    private static void CheckStartupUi(OrbitApplication app)
    {
        Assert(!app.Settings.StartupPromptCompleted, "fresh or old settings need first-run choice");
        Assert(app.Settings.ShouldOfferStartup(false, false), "manual first launch prompts");
        Assert(!app.Settings.ShouldOfferStartup(true, false), "background launch stays silent");
        Assert(!app.Settings.ShouldOfferStartup(false, true), "existing registration does not prompt");
        var attempts = 0;
        var prompt = new StartupWindow(app, () => { if (++attempts == 1) throw new UnauthorizedAccessException("test registration denied"); }) { Owner = app.MainWindow };
        try
        {
            prompt.Show(); prompt.UpdateLayout();
            var enable = Descendants<Button>(prompt).Single(b => Equals(b.Content, "Bật cùng Windows"));
            enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(prompt.IsVisible && !app.Settings.StartupPromptCompleted, "failed registration remains retryable");
            Assert(Descendants<TextBlock>(prompt).Any(t => t.Text.Contains("test registration denied")), "actionable error displayed");
            enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(attempts == 2 && !prompt.IsVisible && AppSettings.Load().StartupPromptCompleted, "successful choice persisted");
            Assert(!app.Settings.ShouldOfferStartup(false, false), "does not re-prompt even if later disabled");
        }
        finally { if (prompt.IsVisible) prompt.Close(); }

        app.Settings.StartupPromptCompleted = false;
        var declined = new StartupWindow(app, () => throw new Exception("decline must never register")) { Owner = app.MainWindow };
        try
        {
            declined.Show(); declined.UpdateLayout();
            Descendants<Button>(declined).Single(b => Equals(b.Content, "Không bật")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!declined.IsVisible && AppSettings.Load().StartupPromptCompleted, "decline persisted");
        }
        finally { if (declined.IsVisible) declined.Close(); }

        app.Settings.StartupPromptCompleted = false;
        var dismissed = new StartupWindow(app, () => throw new Exception("close must never register")) { Owner = app.MainWindow };
        dismissed.Show(); dismissed.Close();
        Assert(AppSettings.Load().StartupPromptCompleted, "X/Esc dismissal persists without registration");
    }
}
