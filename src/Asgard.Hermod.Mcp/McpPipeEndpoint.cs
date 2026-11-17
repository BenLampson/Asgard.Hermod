using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Asgard.Hermod.Mcp;

/// <summary>传输仅限同一 Windows 用户的登录会话，凭证只用于双向挑战应答。</summary>
internal static partial class McpPipeEndpoint
{
    internal const string SecretVariable = "HERMOD_MCP_TOKEN";
    internal const string Executable = "Hermod.Mcp.exe";
    private const int NonceBytes = 32;
    private const int ProofBytes = 32;
    private static readonly int Session = Process.GetCurrentProcess().SessionId;
    internal static SecurityIdentifier User { get; } = CurrentSid();
    internal static string Name { get; } = "Hermod.Mcp." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"Hermod.Mcp.v1:{User.Value}:{Session.ToString(System.Globalization.CultureInfo.InvariantCulture)}")));

    internal static bool IsSecret(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    internal static bool IsLocalPeer(PipeStream pipe, bool peerIsServer)
    {
        bool found = peerIsServer ? GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)
            : GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid);
        if (!found || pid == 0 || pid > int.MaxValue) return false;
        using var process = Process.GetProcessById((int)pid);
        return process.SessionId == Session;
    }

    internal static async Task<bool> AcceptAsync(Stream pipe, string secret, CancellationToken token)
    {
        byte[] challenge = new byte[NonceBytes * 2];
        await pipe.ReadExactlyAsync(challenge.AsMemory(0, NonceBytes), token).ConfigureAwait(false);
        RandomNumberGenerator.Fill(challenge.AsSpan(NonceBytes));
        await pipe.WriteAsync(challenge.AsMemory(NonceBytes), token).ConfigureAwait(false);
        await pipe.WriteAsync(Proof(secret, challenge, server: true), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
        byte[] proof = new byte[ProofBytes];
        await pipe.ReadExactlyAsync(proof, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(proof, Proof(secret, challenge, server: false))) return false;
        await pipe.WriteAsync(new byte[] { 1 }, token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
        return true;
    }

    internal static async Task<bool> AuthenticateAsync(Stream pipe, string secret, CancellationToken token)
    {
        byte[] challenge = new byte[NonceBytes * 2];
        RandomNumberGenerator.Fill(challenge.AsSpan(0, NonceBytes));
        await pipe.WriteAsync(challenge.AsMemory(0, NonceBytes), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
        await pipe.ReadExactlyAsync(challenge.AsMemory(NonceBytes), token).ConfigureAwait(false);
        byte[] proof = new byte[ProofBytes];
        await pipe.ReadExactlyAsync(proof, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(proof, Proof(secret, challenge, server: true))) return false;
        await pipe.WriteAsync(Proof(secret, challenge, server: false), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
        byte[] reply = new byte[1];
        await pipe.ReadExactlyAsync(reply, token).ConfigureAwait(false);
        return reply[0] == 1;
    }

    private static byte[] Proof(string secret, byte[] challenge, bool server)
    {
        byte[] key = Convert.FromHexString(secret);
        Span<byte> material = stackalloc byte[NonceBytes * 2 + 1];
        material[0] = server ? (byte)1 : (byte)2;
        challenge.CopyTo(material[1..]);
        try { return HMACSHA256.HashData(key, material); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static SecurityIdentifier CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
