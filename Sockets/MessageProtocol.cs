using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Musical_Encryption_Project.Sockets;

/// <summary>
/// The wire format every message on this TCP connection uses: a 4-byte
/// big-endian length prefix, followed by that many bytes of UTF-8 JSON.
/// Both sides must agree on this exactly — this is a byte-for-byte copy of
/// the SERVER project's Sockets/MessageProtocol.cs.
/// </summary>
public static class MessageProtocol
{
    private const int MaxFrameBytes = 1024 * 1024; // guards against a corrupt/hostile length prefix

    // "username" (an anonymous object's lowercase property) must still bind to a DTO's
    // "Username" property — ASP.NET Core did this for us by default; raw System.Text.Json does not.
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct = default)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
    }

    /// <returns>null if the other side closed the connection cleanly before sending a new frame.</returns>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct))
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > MaxFrameBytes)
            throw new InvalidDataException($"Invalid frame length {length}.");

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct))
            throw new IOException("Connection closed mid-frame.");

        return payload;
    }

    /// <returns>false only if the stream was already closed before any byte of this frame arrived.</returns>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
                return false;
            offset += read;
        }
        return true;
    }
}

/// <summary>One call from client to server: which action to run, and its JSON body.</summary>
public record SocketRequest(string Action, JsonElement? Payload)
{
    public static SocketRequest Create(string action, object? payload = null) =>
        new(action, payload is null ? null : JsonSerializer.SerializeToElement(payload));

    public byte[] ToFrame() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static SocketRequest FromFrame(byte[] frame) =>
        JsonSerializer.Deserialize<SocketRequest>(frame) ?? throw new InvalidDataException("Empty request frame.");

    public T GetPayload<T>() =>
        Payload is { } p ? p.Deserialize<T>(MessageProtocol.JsonOptions)! : throw new InvalidDataException("Request has no payload.");
}

/// <summary>The server's reply to one SocketRequest.</summary>
public record SocketResponse(bool Success, int StatusCode, string? Error, JsonElement? Payload)
{
    public static SocketResponse Ok(int statusCode, object? payload = null) =>
        new(true, statusCode, null, payload is null ? null : JsonSerializer.SerializeToElement(payload));

    public static SocketResponse Fail(int statusCode, string error) => new(false, statusCode, error, null);

    public byte[] ToFrame() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static SocketResponse FromFrame(byte[] frame) =>
        JsonSerializer.Deserialize<SocketResponse>(frame) ?? throw new InvalidDataException("Empty response frame.");

    public T? GetPayload<T>() => Payload is { } p ? p.Deserialize<T>(MessageProtocol.JsonOptions) : default;
}
