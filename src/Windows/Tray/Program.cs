using System.Diagnostics;
using System.Text.Json;
using BackThread.Host;
using BackThread.Media;

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
        menu.Items.Add("Send Media to iPhone...", null, (_, _) => ShowSendMediaDialog());
        menu.Items.Add("Return Playback to Windows", null, async (_, _) =>
        {
            try { await runtime.ReturnToWindowsAsync(resumeWindows: true, stop.Token); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "BackThread", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        });

        var focusMenu = new ToolStripMenuItem("Audio Focus");
        foreach (AudioFocusPolicy policy in new[]
        {
            AudioFocusPolicy.Auto,
            AudioFocusPolicy.IndependentOnly,
            AudioFocusPolicy.LivePcmOnly,
            AudioFocusPolicy.KeepVideoMuteAudio,
            AudioFocusPolicy.DuckVideo
        })
        {
            var captured = policy;
            focusMenu.DropDownItems.Add(policy.ToString(), null, async (_, _) =>
            {
                try { await runtime.SetFocusPolicyAsync(captured, stop.Token); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "BackThread", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            });
        }
        menu.Items.Add(focusMenu);

        var spotifyMenu = new ToolStripMenuItem("Spotify");
        spotifyMenu.DropDownItems.Add("Connect via OAuth PKCE...", null, (_, _) => ConnectSpotify());
        spotifyMenu.DropDownItems.Add("Disconnect Spotify", null, (_, _) => { runtime.Spotify.Disconnect(); MessageBox.Show("Spotify token cleared.", "BackThread"); });
        menu.Items.Add(spotifyMenu);

        var notifyMenu = new ToolStripMenuItem("Notifications");
        notifyMenu.DropDownItems.Add("Request Windows Notification Permission", null, async (_, _) =>
        {
            string status = await runtime.RequestNotificationAccessAsync();
            MessageBox.Show($"Windows notification listener status: {status}", "BackThread Notifications");
        });
        menu.Items.Add(notifyMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Connection and diagnostics", null, (_, _) =>
        {
            string text;
            lock (logs) text = string.Join(Environment.NewLine, logs);
            MessageBox.Show(JsonSerializer.Serialize(runtime.Status) + Environment.NewLine + Environment.NewLine + text, "BackThread diagnostics");
        });
        menu.Items.Add("Open configuration", null, (_, _) => Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { config }, UseShellExecute = false }));
        menu.Items.Add("Exit", null, async (_, _) => { stop.Cancel(); try { await hostTask; } catch (Exception) { } ExitThread(); });

        icon = new() { Icon = System.Drawing.SystemIcons.Application, Text = "BackThread", ContextMenuStrip = menu, Visible = true };
        timer.Tick += (_, _) =>
        {
            var m = runtime.Router.CurrentState;
            string modeSummary = m.Owner == PlaybackOwner.iPhoneOwned ? $"PiP: {m.Mode}" : "running";
            icon.Text = hostTask.IsFaulted
                ? "BackThread — host failed; open diagnostics"
                : $"BackThread — {modeSummary}";
        };
        timer.Start();
    }

    private void ConnectSpotify()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(runtime.Config.SpotifyClientId))
            {
                MessageBox.Show("Configure spotifyClientId in config/local.json first.", "BackThread Spotify");
                return;
            }
            var auth = runtime.Spotify.BeginAuthorization(runtime.Config.SpotifyClientId, runtime.Config.SpotifyRedirectUri);
            Process.Start(new ProcessStartInfo(auth.AuthorizeUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "BackThread Spotify", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSendMediaDialog()
    {
        using var dialog = new Form
        {
            Text = "Send Media to iPhone (External PiP)",
            Width = 540,
            Height = 360,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 8, ColumnCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var urlBox = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "https://... (.mp4, .m3u8, .mp3, Spotify, YouTube) or local path" };
        var browseBtn = new Button { Text = "File...", AutoSize = true };
        browseBtn.Click += (_, _) =>
        {
            using var open = new OpenFileDialog
            {
                Filter = "Supported Media|*.mp4;*.m4v;*.mov;*.mp3;*.m4a;*.aac;*.wav|All Files|*.*"
            };
            if (open.ShowDialog() == DialogResult.OK) urlBox.Text = open.FileName;
        };

        var modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        modeBox.Items.AddRange([PlaybackMode.Auto, PlaybackMode.DirectInternet, PlaybackMode.BufferedUsb, PlaybackMode.LivePcm, PlaybackMode.RemoteControl]);
        modeBox.SelectedIndex = 0;

        var qualityBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        qualityBox.Items.AddRange(["auto", "720p", "480p", "audio-only"]);
        qualityBox.SelectedItem = runtime.Config.DefaultQualityCap;

        var awakeCheck = new CheckBox { Text = "Keep iPhone screen awake during video", Checked = runtime.Config.KeepScreenAwakeDuringVideo, AutoSize = true };
        var pauseWinCheck = new CheckBox { Text = "Pause Windows media after iPhone confirms playback", Checked = runtime.Config.PauseWindowsOnConfirmedHandoff, AutoSize = true };
        var statusLabel = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(490, 60), Text = "Ready." };

        var sendBtn = new Button { Text = "Inspect & Send", AutoSize = true };
        var cancelBtn = new Button { Text = "Close", AutoSize = true };
        cancelBtn.Click += (_, _) => dialog.Close();

        sendBtn.Click += async (_, _) =>
        {
            sendBtn.Enabled = false;
            try
            {
                string text = urlBox.Text.Trim();
                bool isFile = !string.IsNullOrEmpty(text) && Path.IsPathFullyQualified(text) && File.Exists(text);
                var req = new MediaSubmissionRequest(
                    Url: isFile ? null : text,
                    FilePath: isFile ? text : null,
                    Mode: (PlaybackMode)(modeBox.SelectedItem ?? PlaybackMode.Auto),
                    QualityCap: qualityBox.SelectedItem?.ToString(),
                    KeepScreenAwake: awakeCheck.Checked,
                    AutoPauseWindowsOnConfirm: pauseWinCheck.Checked);

                statusLabel.Text = "Inspecting capabilities and requesting handoff...";
                var result = await runtime.SubmitMediaAsync(req, stop.Token);
                statusLabel.Text = result.Error is { Length: > 0 } err
                    ? $"{result.Owner} ({result.Stage}): {err}"
                    : $"{result.Owner} · {result.Mode} · {result.Stage} — {result.Capabilities?.Reason}";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                sendBtn.Enabled = true;
            }
        };

        layout.Controls.Add(new Label { Text = "URL or File:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(urlBox, 1, 0);
        layout.Controls.Add(browseBtn, 2, 0);

        layout.Controls.Add(new Label { Text = "Playback Mode:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(modeBox, 1, 1);

        layout.Controls.Add(new Label { Text = "Quality Cap:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(qualityBox, 1, 2);

        layout.Controls.Add(awakeCheck, 1, 3);
        layout.Controls.Add(pauseWinCheck, 1, 4);
        layout.Controls.Add(statusLabel, 0, 5);
        layout.SetColumnSpan(statusLabel, 3);

        var buttonRow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        buttonRow.Controls.Add(cancelBtn);
        buttonRow.Controls.Add(sendBtn);
        layout.Controls.Add(buttonRow, 0, 6);
        layout.SetColumnSpan(buttonRow, 3);

        dialog.Controls.Add(layout);
        dialog.ShowDialog();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stop.Cancel();
            timer.Dispose();
            icon.Visible = false;
            icon.Dispose();
            runtime.Dispose();
            stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
