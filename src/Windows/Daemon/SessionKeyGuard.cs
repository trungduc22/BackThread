using System.Runtime.Versioning;
using BackThread.Transport;
using NativeShellKit.Security;

namespace BackThread.Daemon;

/// <summary>
/// Ensures the 32-byte BackThread session key exists and keeps a DPAPI (CurrentUser + AppIdentity entropy)
/// protected backup so a deleted/corrupted <c>session.key</c> can be restored without re-provisioning the iPhone.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SessionKeyGuard
{
    public const string SecretName = "session_key";

    public static async Task<string> EnsureAsync(string keyPath, DpapiSecretStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        string full = Path.GetFullPath(keyPath);

        if (File.Exists(full))
        {
            string hex = Convert.ToHexString(Authentication.LoadKey(full)).ToLowerInvariant();
            string? backup = await store.TryLoadSecretAsync(SecretName, ct).ConfigureAwait(false);
            if (!string.Equals(backup, hex, StringComparison.Ordinal))
                await store.SaveSecretAsync(SecretName, hex, ct).ConfigureAwait(false);
            return "file";
        }

        string? saved = await store.TryLoadSecretAsync(SecretName, ct).ConfigureAwait(false);
        if (IsValidHexKey(saved))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await using (var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(file))
                await writer.WriteAsync(saved!.ToLowerInvariant()).ConfigureAwait(false);
            return "restored-from-dpapi";
        }

        Authentication.CreateKey(full);
        string created = Convert.ToHexString(Authentication.LoadKey(full)).ToLowerInvariant();
        await store.SaveSecretAsync(SecretName, created, ct).ConfigureAwait(false);
        return "generated";
    }

    internal static bool IsValidHexKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != 64)
            return false;
        try { return Convert.FromHexString(value.Trim()).Length == 32; }
        catch (FormatException) { return false; }
    }
}
