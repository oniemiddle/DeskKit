using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskKit.Widgets.TickTick;

/// <summary>Small client for TickTick's documented Open API.</summary>
internal sealed class TickTickApiClient : IDisposable
{
    private const string ApiBase = "https://api.ticktick.com/open/v1/";
    private const string Scope = "tasks:read tasks:write";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = new();

    public async Task<string> AuthorizeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect)
            || redirect.Scheme != Uri.UriSchemeHttp
            || !IPAddress.TryParse(redirect.Host, out var loopback)
            || !IPAddress.IsLoopback(loopback))
        {
            throw new InvalidOperationException("The redirect URL must use HTTP on a loopback address, such as http://127.0.0.1:43821/callback.");
        }

        using var listener = new TcpListener(loopback, redirect.Port);
        listener.Start();

        var state = Guid.NewGuid().ToString("N");
        var authorizeUrl = "https://ticktick.com/oauth/authorize?" + FormEncode(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope,
            ["state"] = state,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
        });

        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var callback = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
        await using var stream = callback.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)))
        {
        }

        var requestTarget = requestLine?.Split(' ');
        var callbackUri = requestTarget is { Length: >= 2 }
            ? new Uri(new Uri(redirect.GetLeftPart(UriPartial.Authority)), requestTarget[1])
            : redirect;
        var query = ParseQuery(callbackUri.Query);
        var responseBody = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>DeskKit</title><p>You can close this window and return to DeskKit.</p>");
        var responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeader, timeout.Token).ConfigureAwait(false);
        await stream.WriteAsync(responseBody, timeout.Token).ConfigureAwait(false);

        if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
            throw new InvalidOperationException("TickTick authorization could not be verified. Please try connecting again.");
        if (query.TryGetValue("error", out var error))
            throw new InvalidOperationException($"TickTick authorization failed: {error}");
        if (!query.TryGetValue("code", out var code))
            throw new InvalidOperationException("TickTick did not return an authorization code.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ticktick.com/oauth/token");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["scope"] = Scope,
            ["redirect_uri"] = redirectUri,
        });

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("TickTick returned an empty access token.");
    }

    public async Task<IReadOnlyList<TickTickTask>> GetTasksAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "project", token);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var projects = await response.Content.ReadFromJsonAsync<List<TickTickProject>>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        var tasks = new List<TickTickTask>();

        foreach (var project in projects.Where(project => !project.Closed && !string.Equals(project.Kind, "NOTE", StringComparison.OrdinalIgnoreCase)))
        {
            using var dataRequest = CreateRequest(HttpMethod.Get, $"project/{Uri.EscapeDataString(project.Id)}/data", token);
            using var dataResponse = await _http.SendAsync(dataRequest, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(dataResponse, cancellationToken).ConfigureAwait(false);
            var data = await dataResponse.Content.ReadFromJsonAsync<TickTickProjectData>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (data?.Tasks is not null)
            {
                foreach (var task in data.Tasks)
                    task.ListName = project.Name;
                tasks.AddRange(data.Tasks);
            }
        }

        return tasks.OrderBy(task => task.Status == 2)
            .ThenBy(task => task.DueDate is null ? 1 : 0)
            .ThenBy(task => DateTimeOffset.TryParse(task.DueDate, out var due) ? due : DateTimeOffset.MaxValue)
            .ThenBy(task => task.SortOrder)
            .ToArray();
    }

    public async Task UpdateTaskAsync(string token, TickTickTask task, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"task/{Uri.EscapeDataString(task.Id)}", token);
        request.Content = JsonContent.Create(task, options: JsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCompletedAsync(string token, TickTickTask task, bool completed, CancellationToken cancellationToken)
    {
        if (completed)
        {
            using var request = CreateRequest(HttpMethod.Post,
                $"project/{Uri.EscapeDataString(task.ProjectId)}/task/{Uri.EscapeDataString(task.Id)}/complete", token);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return;
        }

        task.Status = 0;
        task.CompletedTime = null;
        await UpdateTaskAsync(token, task, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(ApiBase), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"TickTick returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}", null, response.StatusCode);
    }

    private static string FormEncode(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            values[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] = pair.Length > 1
                ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                : string.Empty;
        }
        return values;
    }

    public void Dispose() => _http.Dispose();
}
