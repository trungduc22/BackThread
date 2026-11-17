using System.Security.Cryptography;
using System.Text;

namespace BackThread.Transport;

public static class Authentication
{
    public static byte[] LoadKey(string path)
    {
        var key = Convert.FromHexString(File.ReadAllText(path).Trim());
        return key.Length == 32 ? key : throw new InvalidDataException("Key must be 32 random bytes encoded as hex");
    }
    public static void CreateKey(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
        file.Write(bytes);
    }
    public static string Proof(byte[] key, string role, string server, string client)
        => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"BTH1:{role}:{server}:{client}"))).ToLowerInvariant();
    private static bool Verify(string actual, string expected)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)); }
        catch (FormatException) { return false; }
    }
    private static string Nonce(Message m, string property)
    {
        var value = m.Body.GetProperty(property).GetString() ?? "";
        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid nonce");
        return value;
    }
    public static async Task ClientAsync(Stream stream, byte[] key, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var frame = await Wire.ReadAsync(stream, timeout.Token);
        var challenge = Message.Decode(frame.Payload);
        if (frame.Channel != LogicalChannel.Control || challenge.Type != "auth.challenge") throw new InvalidDataException("Expected challenge");
        string server = Nonce(challenge, "serverNonce"), client = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await Wire.WriteAsync(stream, new(LogicalChannel.Control, 0, 0, Message.Create("auth.response", new { clientNonce = client, proof = Proof(key, "host", server, client) }).Encode()), timeout.Token);
        var reply = await Wire.ReadAsync(stream, timeout.Token);
        var ok = Message.Decode(reply.Payload);
        if (reply.Channel != LogicalChannel.Control || ok.Type != "auth.ok" || !Verify(ok.Body.GetProperty("proof").GetString() ?? "", Proof(key, "agent", server, client)))
            throw new UnauthorizedAccessException("Agent authentication failed");
    }
    public static async Task ServerAsync(Stream stream, byte[] key, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        string server = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await Wire.WriteAsync(stream, new(LogicalChannel.Control, 0, 0, Message.Create("auth.challenge", new { serverNonce = server }).Encode()), timeout.Token);
        var frame = await Wire.ReadAsync(stream, timeout.Token);
        var auth = Message.Decode(frame.Payload);
        if (frame.Channel != LogicalChannel.Control || auth.Type != "auth.response") throw new UnauthorizedAccessException("Expected authentication");
        string client = Nonce(auth, "clientNonce");
        if (!Verify(auth.Body.GetProperty("proof").GetString() ?? "", Proof(key, "host", server, client))) throw new UnauthorizedAccessException("Host authentication failed");
        await Wire.WriteAsync(stream, new(LogicalChannel.Control, 0, 0, Message.Create("auth.ok", new { proof = Proof(key, "agent", server, client), protocol = 1 }).Encode()), timeout.Token);
    }
}
