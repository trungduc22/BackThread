using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace BackThread.Transport;

/// <summary>
/// Manages the per-PC self-signed ECDSA P-256 TLS certificate used for pinned Wi-Fi / Tailscale connections.
/// The phone pins the exact SHA-256 certificate fingerprint obtained via QR pairing or USB auto-provision.
/// </summary>
public sealed class LanCertificateStore
{
    public static X509Certificate2 LoadOrCreate(string certPath, string? machineName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certPath);
        string fullPath = Path.GetFullPath(certPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath))
        {
            RestrictToOwner(fullPath);
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(
                    fullPath,
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
            }
            catch
            {
                // Corrupt certificate file; regenerate cleanly below.
            }
        }

        using var cert = CreateEphemeral(machineName);
        byte[] pfx = cert.Export(X509ContentType.Pfx);
        WritePrivateFile(fullPath, pfx);
        return X509CertificateLoader.LoadPkcs12FromFile(
            fullPath,
            password: null,
            keyStorageFlags: X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    /// <summary>Creates an in-memory self-signed ECDSA P-256 server certificate (useful for tests).</summary>
    public static X509Certificate2 CreateEphemeral(string? machineName = null)
    {
        string cn = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim();
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=BackThread Host ({cn})", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        byte[] pfx = created.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null, keyStorageFlags: X509KeyStorageFlags.Exportable);
    }

    /// <summary>Lowercase 64-character SHA-256 fingerprint of the DER-encoded certificate.</summary>
    public static string GetFingerprint(X509Certificate2 certificate)
        => Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    /// <summary>Short deterministic PC identifier derived from the certificate fingerprint.</summary>
    public static string GetPcId(string fingerprint)
        => fingerprint.Length >= 16 ? fingerprint[..16].ToLowerInvariant() : fingerprint.ToLowerInvariant();

    /// <summary>Human-readable 8-character check code ("AB12 CD34") shown on both PC and iPhone during pairing.</summary>
    public static string GetHumanCheckCode(string fingerprint)
        => fingerprint.Length < 8
            ? fingerprint.ToUpperInvariant()
            : $"{fingerprint[..4]} {fingerprint[4..8]}".ToUpperInvariant();

    private static void WritePrivateFile(string path, byte[] data)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data);
            fs.Flush(flushToDisk: true);
        }
        RestrictToOwner(tmp);
        File.Move(tmp, path, overwrite: true);
        RestrictToOwner(path);
    }

    internal static void RestrictToOwner(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                var user = WindowsIdentity.GetCurrent().User;
                if (user is not null)
                {
                    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                        security.RemoveAccessRule(rule);
                    security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                    info.SetAccessControl(security);
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best effort on filesystems without ACL/POSIX mode support.
        }
    }
}
