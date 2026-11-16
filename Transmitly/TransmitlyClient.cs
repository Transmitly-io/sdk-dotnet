using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Transmitly;

/// <summary>
/// Dependency-free client for the Transmitly email API.
///
/// <code>
/// var tx = new TransmitlyClient(Environment.GetEnvironmentVariable("TRANSMITLY_API_KEY")!);
/// var accepted = await tx.Emails.SendAsync(new EmailMessage
/// {
///     From = "Notify &lt;notify@mail.acme.com&gt;",
///     To = new[] { "ada@example.net" },
///     Subject = "Your workspace is ready",
///     Html = "&lt;p&gt;Welcome.&lt;/p&gt;",
/// });
/// </code>
/// </summary>
public sealed class TransmitlyClient : IDisposable
{
    public const string DefaultBaseUrl = "https://transmitly.io";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;

    public string BaseUrl { get; }
    public EmailsResource Emails { get; }
    public MessagesResource Messages { get; }
    public DomainsResource Domains { get; }
    public SuppressionsResource Suppressions { get; }
    public WebhooksResource Webhooks { get; }
    public InboundResource Inbound { get; }
    public UsageResource Usage { get; }
    public EventsResource Events { get; }

    public TransmitlyClient(
        string apiKey,
        string? baseUrl = null,
        TimeSpan? timeout = null,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("apiKey is required", nameof(apiKey));
        }

        _apiKey = apiKey;
        BaseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _timeout = timeout ?? DefaultTimeout;
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;

        Emails = new EmailsResource(this);
        Messages = new MessagesResource(this);
        Domains = new DomainsResource(this);
        Suppressions = new SuppressionsResource(this);
        Webhooks = new WebhooksResource(this);
        Inbound = new InboundResource(this);
        Usage = new UsageResource(this);
        Events = new EventsResource(this);
    }

    public Task<JsonElement> HealthAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(HttpMethod.Get, "/v1/health", auth: false, cancellationToken: cancellationToken);

    /// <summary>
    /// Verify a webhook delivery. Pass the raw request body (not a parsed JSON
    /// object), the Transmitly-Signature header and the endpoint signing secret.
    /// </summary>
    public static bool VerifyWebhook(string rawBody, string? signatureHeader, string secret, int toleranceSeconds = 300)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        long? timestamp = null;
        string? signature = null;
        foreach (var part in signatureHeader.Split(','))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }
            if (pair[0] == "t")
            {
                timestamp = long.TryParse(pair[1], out var parsed) ? parsed : null;
            }
            else if (pair[0] == "v1")
            {
                signature = pair[1];
            }
        }

        if (timestamp is null || string.IsNullOrEmpty(signature))
        {
            return false;
        }
        if (toleranceSeconds > 0 && Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp.Value) > toleranceSeconds)
        {
            return false;
        }

        var expected = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}")))
            .ToLowerInvariant();
        if (signature.Length != expected.Length)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(signature),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal async Task<JsonElement> RequestAsync(
        HttpMethod method,
        string path,
        object? body = null,
        IReadOnlyDictionary<string, string?>? query = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool auth = true,
        CancellationToken cancellationToken = default)
    {
        var url = BaseUrl + path;
        if (query is not null)
        {
            var parts = query
                .Where(pair => pair.Value is not null && pair.Value != "")
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}")
                .ToArray();
            if (parts.Length > 0)
            {
                url += "?" + string.Join("&", parts);
            }
        }

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (auth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonDefaults.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransmitlyException(0, "timeout", "Request timed out");
        }
        catch (HttpRequestException error)
        {
            throw new TransmitlyException(0, "network_error", error.Message);
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            JsonElement? payload = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    using var document = JsonDocument.Parse(raw);
                    payload = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    throw new TransmitlyException(
                        (int)response.StatusCode,
                        "invalid_response",
                        "Could not decode JSON response",
                        payload: raw);
                }
            }

            if (response.IsSuccessStatusCode)
            {
                return payload ?? default;
            }

            var code = "";
            var message = $"Request failed ({(int)response.StatusCode})";
            JsonElement? details = null;
            if (payload is { } value && value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String)
                {
                    code = codeValue.GetString() ?? "";
                }
                if (error.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String)
                {
                    message = messageValue.GetString() ?? message;
                }
                if (error.TryGetProperty("details", out var detailsValue))
                {
                    details = detailsValue.Clone();
                }
            }

            throw new TransmitlyException((int)response.StatusCode, code, message, details, raw);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
