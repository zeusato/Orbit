using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Orbit.App.Services;

// Explorer right-click "Orbit" with a cascading submenu per file type, written to the current
// user's classes (no administrator). Windows 11 lists it under "Show more options".
public static class ShellMenu
{
    private const string Prefix = "Orbit.Menu.";
    public static string ClassesPath => @"Software\Classes";

    public static void Register(string exe, RegistryKey? classes = null)
    {
        using var owned = classes == null ? Registry.CurrentUser.CreateSubKey(ClassesPath) : null;
        var root = classes ?? owned!;
        Unregister(root);
        var icon = $"\"{exe}\",0";
        foreach (var kind in ShellMenuLayout.Extensions.Values.Distinct())
        {
            using var menu = root.CreateSubKey($@"{Prefix}{kind}\shell");
            var tools = ShellMenuLayout.Tools(kind, OfficeConverter.Available);
            using (var open = menu.CreateSubKey("00open"))
            {
                open.SetValue("MUIVerb", "Mở menu tròn Orbit…"); open.SetValue("Icon", icon); open.SetValue("MultiSelectModel", "Player");
                open.SetValue("CommandFlags", 0x40, RegistryValueKind.DWord); // separator after
                using var command = open.CreateSubKey("command"); command.SetValue("", $"\"{exe}\" --open \"%1\"");
            }
            for (int i = 0; i < tools.Count; i++)
            {
                using var verb = menu.CreateSubKey($"{i + 1:D2}{tools[i]}");
                verb.SetValue("MUIVerb", ShellMenuLayout.Label(tools[i])); verb.SetValue("MultiSelectModel", "Player");
                if (tools[i] == ToolKind.Zip) verb.SetValue("CommandFlags", 0x20, RegistryValueKind.DWord); // separator before generic tools
                using var command = verb.CreateSubKey("command"); command.SetValue("", $"\"{exe}\" --tool {tools[i]} \"%1\"");
            }
        }
        foreach (var (extension, kind) in ShellMenuLayout.Extensions)
        {
            // Another program's verb that happens to be called "Orbit" is left alone.
            using (var existing = root.OpenSubKey($@"SystemFileAssociations\{extension}\shell\Orbit"))
                if (existing != null && !(existing.GetValue("ExtendedSubCommandsKey") is string current && current.StartsWith(Prefix, StringComparison.Ordinal))) continue;
            using var verb = root.CreateSubKey($@"SystemFileAssociations\{extension}\shell\Orbit");
            verb.SetValue("MUIVerb", "Orbit"); verb.SetValue("Icon", icon); verb.SetValue("MultiSelectModel", "Player");
            verb.SetValue("ExtendedSubCommandsKey", Prefix + kind);
        }
        if (classes == null) Changed();
    }

    // Removes only keys Orbit wrote: the Orbit.Menu.* trees and "Orbit" verbs that point at them.
    public static void Unregister(RegistryKey? classes = null)
    {
        using var owned = classes == null ? Registry.CurrentUser.OpenSubKey(ClassesPath, true) : null;
        var root = classes ?? owned; if (root == null) return;
        foreach (var name in root.GetSubKeyNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))) root.DeleteSubKeyTree(name, false);
        foreach (var extension in ShellMenuLayout.Extensions.Keys)
        {
            using var shell = root.OpenSubKey($@"SystemFileAssociations\{extension}\shell", true);
            using var verb = shell?.OpenSubKey("Orbit");
            if (verb?.GetValue("ExtendedSubCommandsKey") is string target && target.StartsWith(Prefix, StringComparison.Ordinal)) { verb.Dispose(); shell!.DeleteSubKeyTree("Orbit", false); }
        }
        if (classes == null) Changed();
    }
    public static bool IsRegistered(string exe)
    {
        using var verb = Registry.CurrentUser.OpenSubKey($@"{ClassesPath}\{Prefix}{FileKind.Png}\shell\00open\command");
        return verb?.GetValue("") is string command && command.StartsWith($"\"{exe}\"", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    private static void Changed() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
}

// The first instance listens from Main, before WPF starts, so a multi-select burst does not
// time out during a cold start; requests wait here until the application attaches.
public static class ShellInbox
{
    private static readonly object Gate = new();
    private static readonly List<ShellCommand> Early = [];
    private static Action<ShellCommand>? _handler;
    public static readonly CancellationTokenSource Stop = new();
    public static void Start() => _ = ShellPipe.Listen(Receive, Stop.Token);
    private static void Receive(ShellCommand command)
    {
        Action<ShellCommand>? handler;
        lock (Gate) { handler = _handler; if (handler == null) Early.Add(command); }
        handler?.Invoke(command);
    }
    public static void Attach(Action<ShellCommand> handler)
    {
        ShellCommand[] pending;
        lock (Gate) { _handler = handler; pending = Early.ToArray(); Early.Clear(); }
        foreach (var command in pending) handler(command);
    }
}

// Second Orbit processes started by Explorer hand their request to the running one.
public static class ShellPipe
{
    public static string DefaultName => "Orbit.Shell." + Environment.UserName;

    public static bool Send(ShellCommand command, string? name = null, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", name ?? DefaultName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                pipe.Connect(500);
                JsonSerializer.Serialize(pipe, command.ToArgs()); pipe.Flush(); return true;
            }
            catch (Exception error) when (error is TimeoutException or IOException) { Thread.Sleep(150); } // The first instance may still be starting.
        }
        return false;
    }

    public static async Task Listen(Action<ShellCommand> received, CancellationToken token, string? name = null)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name ?? DefaultName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var buffer = new MemoryStream(); var chunk = new byte[4096]; int read;
                while ((read = await pipe.ReadAsync(chunk, token)) > 0) { buffer.Write(chunk, 0, read); if (buffer.Length > 1 << 20) break; }
                var args = JsonSerializer.Deserialize<string[]>(buffer.ToArray());
                if (args != null && ShellCommand.TryParse(args, out var command)) received(command);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { await Task.Delay(100, CancellationToken.None); }
        }
    }
}
