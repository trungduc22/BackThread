using BackThread.Host;
using System.Text.Json;

namespace BackThread.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string config = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "config", "local.json");
        try { Application.Run(new TrayContext(config)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "BackThread", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon icon;
    private readonly CancellationTokenSource stop = new();
    private readonly Task hostTask;
    private readonly HostRuntime runtime;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly Queue<string> logs = new();
    public TrayContext(string config)
    {
        // Relative key paths in a configuration are resolved against its parent project directory.
        Directory.SetCurrentDirectory(Path.GetDirectoryName(Path.GetDirectoryName(config))!);
        runtime = new(HostConfig.Load(config));
        runtime.Log += message => { lock (logs) { logs.Enqueue(message); while (logs.Count > 30) logs.Dequeue(); } };
        hostTask = Task.Run(() => runtime.RunAsync(stop.Token));
        var menu = new ContextMenuStrip();
        menu.Items.Add("Connection and diagnostics", null, (_, _) => { string text; lock (logs) text = string.Join(Environment.NewLine, logs); MessageBox.Show(JsonSerializer.Serialize(runtime.Status) + Environment.NewLine + text, "BackThread diagnostics"); });
        menu.Items.Add("Open configuration", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe") { ArgumentList = { config }, UseShellExecute = false }));
        menu.Items.Add("Exit", null, async (_, _) => { stop.Cancel(); try { await hostTask; } catch (Exception) { } ExitThread(); });
        icon = new() { Icon = System.Drawing.SystemIcons.Application, Text = "BackThread", ContextMenuStrip = menu, Visible = true };
        timer.Tick += (_, _) => { icon.Text = hostTask.IsFaulted ? "BackThread — host failed; open diagnostics" : "BackThread — running"; }; timer.Start();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { stop.Cancel(); timer.Dispose(); icon.Visible = false; icon.Dispose(); stop.Dispose(); }
        base.Dispose(disposing);
    }
}
