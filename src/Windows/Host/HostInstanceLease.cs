namespace BackThread.Host;

internal static class HostInstanceLease
{
    public static FileStream Acquire(HostConfig config)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackThread", "runtime");
        Directory.CreateDirectory(directory);
        // A fixture uses its own port. Real USB hosts share a lease even with different API ports.
        var name = config.Transport == "usb" ? "usb.lock" : $"localhost-{config.ApiPort}.lock";
        try
        {
            return new FileStream(Path.Combine(directory, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException("BackThread could not acquire the host lease. A host may already be running for this user.", exception);
        }
    }
}
