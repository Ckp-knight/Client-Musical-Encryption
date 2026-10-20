using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Musical_Encryption_Project.Sockets;

/// <summary>
/// Raw TCP transport: opens a fresh connection for each call (connect, send
/// one framed <see cref="SocketRequest"/>, read one framed
/// <see cref="SocketResponse"/>, close) — the socket equivalent of how
/// <c>ApiClient</c> used to make one HTTP request per call. Host/Port must
/// match the SERVER's <c>Sockets/TcpSocketServer.Port</c>.
/// </summary>
public class SocketApiClient
{
    private readonly string _host;
    private readonly int _port;

    public SocketApiClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task<SocketResponse> SendAsync(SocketRequest request, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, ct);

        var stream = client.GetStream();
        await MessageProtocol.WriteFrameAsync(stream, request.ToFrame(), ct);

        var frame = await MessageProtocol.ReadFrameAsync(stream, ct)
            ?? throw new IOException("Server closed the connection without responding.");

        return SocketResponse.FromFrame(frame);
    }
}
