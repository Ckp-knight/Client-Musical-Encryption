using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Musical_Encryption_Project;

/// <summary>
/// The only place in this project that talks to the server. Everything else
/// (MainWindow, etc.) calls these methods and never touches HttpClient directly.
/// Points at the local dev server — change <see cref="BaseUrl"/> when you deploy.
/// </summary>
public class ApiClient : IDisposable
{
    private const string BaseUrl = "http://localhost:5266";

    private readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl) };

    /// <summary>Result of an API call: either it worked, or here's why not.</summary>
    public record ApiResult(bool Success, string Message);

    public record RegisteredUser(string Id, string Username, DateTimeOffset CreatedAt);

    /// <summary>Same as <see cref="ApiResult"/>, plus the username on success (login needs it, register doesn't).</summary>
    public record LoginResult(bool Success, string Message, string? Username);

    /// <summary>Calls POST /api/users. Returns a human-readable message either way.</summary>
    public async Task<ApiResult> RegisterAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/users", new { username, password });

            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<RegisteredUser>();
                return new ApiResult(true, $"Account '{user?.Username}' created.");
            }

            // 400 (bad input) and 409 (username taken) both come back as plain text.
            var errorText = await response.Content.ReadAsStringAsync();
            return new ApiResult(false, string.IsNullOrWhiteSpace(errorText)
                ? $"Server returned {(int)response.StatusCode}."
                : errorText);
        }
        catch (HttpRequestException ex)
        {
            // The server isn't running / isn't reachable at BaseUrl.
            return new ApiResult(false, $"Could not reach the server at {BaseUrl}. Is it running? ({ex.Message})");
        }
    }

    /// <summary>Calls POST /api/users/login. Same success/error shape as <see cref="RegisterAsync"/>.</summary>
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/users/login", new { username, password });

            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<RegisteredUser>();
                return new LoginResult(true, "Logged in.", user?.Username);
            }

            // 400 (missing fields) and 401 (bad credentials) both come back as plain text.
            var errorText = await response.Content.ReadAsStringAsync();
            return new LoginResult(false, string.IsNullOrWhiteSpace(errorText)
                ? $"Server returned {(int)response.StatusCode}."
                : errorText, null);
        }
        catch (HttpRequestException ex)
        {
            return new LoginResult(false, $"Could not reach the server at {BaseUrl}. Is it running? ({ex.Message})", null);
        }
    }

    public void Dispose() => _http.Dispose();
}
