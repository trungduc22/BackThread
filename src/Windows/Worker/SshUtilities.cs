using System.Diagnostics;

namespace BackThread.Worker;

public static class SshUtilities
{
    /// <summary>OpenSSH only, through an explicitly configured loopback port forward. No password or host-key bypass.</summary>
    public static async Task<string> DiagnosticAsync(int localPort, string user, string keyPath, string knownHostsPath, string diagnostic, CancellationToken ct)
    {
        if (localPort is < 1024 or > 65535 || user.Length == 0 || !user.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) throw new ArgumentException("Invalid SSH endpoint");
        string command = diagnostic switch { "uptime" => "/usr/bin/uptime", "storage" => "/bin/df -k", "identity" => "/usr/bin/id", _ => throw new InvalidDataException("Diagnostic is not authorized") };
        var start = new ProcessStartInfo("ssh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string arg in new[] { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ConnectTimeout=5", "-o", "IdentitiesOnly=yes", "-o", "UserKnownHostsFile=" + Path.GetFullPath(knownHostsPath), "-i", Path.GetFullPath(keyPath), "-p", localPort.ToString(System.Globalization.CultureInfo.InvariantCulture), user + "@127.0.0.1", command }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start OpenSSH");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        // Fixed diagnostics return small output. Enforce the bound as data arrives.
        async Task<string> ReadBounded(StreamReader reader)
        {
            char[] b = new char[1024]; var text = new System.Text.StringBuilder(); int n;
            while ((n = await reader.ReadAsync(b, timeout.Token)) != 0) { if (text.Length + n > 65536) throw new IOException("SSH diagnostic output limit"); text.Append(b, 0, n); }
            return text.ToString();
        }
        try
        {
            var output = ReadBounded(process.StandardOutput); var error = ReadBounded(process.StandardError);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new IOException(await error); return await output;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
