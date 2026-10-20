using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Musical_Encryption_Project.Sockets;

namespace Musical_Encryption_Project;

/// <summary>
/// The only place in this project that talks to the server. Everything else
/// (MainWindow, etc.) calls these methods and never touches the socket layer
/// directly. Points at the local dev server — change Host/Port when you move
/// to another machine on the LAN.
/// </summary>
public class ApiClient : IDisposable
{
    private const string Host = "127.0.0.1";
    private const int Port = 5300; // must match the SERVER's Sockets/TcpSocketServer.Port

    private readonly SocketApiClient _socket = new(Host, Port);

    /// <summary>Result of an API call: either it worked, or here's why not.</summary>
    public record ApiResult(bool Success, string Message);

    public record RegisteredUser(string Id, string Username, DateTimeOffset CreatedAt);

    /// <summary>Same as <see cref="ApiResult"/>, plus the username on success (login needs it, register doesn't).</summary>
    public record LoginResult(bool Success, string Message, string? Username);

    /// <summary>Calls the "register" action. Returns a human-readable message either way.</summary>
    public async Task<ApiResult> RegisterAsync(string username, string password)
    {
        try
        {
            var request = SocketRequest.Create("register", new { username, password });
            var response = await _socket.SendAsync(request);

            if (response.Success)
            {
                var user = response.GetPayload<RegisteredUser>();
                return new ApiResult(true, $"Account '{user?.Username}' created.");
            }

            return new ApiResult(false, response.Error ?? $"Server returned {response.StatusCode}.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            // The server isn't running / isn't reachable at Host:Port.
            return new ApiResult(false, $"Could not reach the server at {Host}:{Port}. Is it running? ({ex.Message})");
        }
    }

    /// <summary>Calls the "login" action. Same success/error shape as <see cref="RegisterAsync"/>.</summary>
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var request = SocketRequest.Create("login", new { username, password });
            var response = await _socket.SendAsync(request);

            if (response.Success)
            {
                var user = response.GetPayload<RegisteredUser>();
                return new LoginResult(true, "Logged in.", user?.Username);
            }

            return new LoginResult(false, response.Error ?? $"Server returned {response.StatusCode}.", null);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return new LoginResult(false, $"Could not reach the server at {Host}:{Port}. Is it running? ({ex.Message})", null);
        }
    }

    /// <summary>Calls the "changePassword" action. Same success/error shape as <see cref="RegisterAsync"/>.</summary>
    public async Task<ApiResult> ChangePasswordAsync(string username, string oldPassword, string newPassword)
    {
        try
        {
            var request = SocketRequest.Create("changePassword", new { username, oldPassword, newPassword });
            var response = await _socket.SendAsync(request);

            if (response.Success)
                return new ApiResult(true, "Password changed. You can now log in with your new password.");

            return new ApiResult(false, response.Error ?? $"Server returned {response.StatusCode}.");
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return new ApiResult(false, $"Could not reach the server at {Host}:{Port}. Is it running? ({ex.Message})");
        }
    }

    public void Dispose() { }
}
