using System.Text.Json;

namespace Transmitly;

public abstract class Resource
{
    protected Resource(TransmitlyClient client)
    {
        Client = client;
    }

    protected TransmitlyClient Client { get; }

    protected static string Path(string id) => Uri.EscapeDataString(id);
}

public sealed class EmailsResource(TransmitlyClient client) : Resource(client)
{
    public async Task<EmailAccepted> SendAsync(
        EmailMessage message,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.From))
        {
            throw new ArgumentException("from is required", nameof(message));
        }
        if (message.To.Count == 0)
        {
            throw new ArgumentException("to is required", nameof(message));
        }
        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            throw new ArgumentException("subject is required", nameof(message));
        }
        if (message.Html is null && message.Text is null && message.TemplateId is null)
        {
            throw new ArgumentException("html, text or template_id is required", nameof(message));
        }

        var headers = new Dictionary<string, string>
        {
            ["Idempotency-Key"] = idempotencyKey ?? Guid.NewGuid().ToString(),
        };
        var payload = await Client.RequestAsync(
            HttpMethod.Post,
            "/v1/emails",
            message,
            headers: headers,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return payload.Deserialize<EmailAccepted>(JsonDefaults.Options)
            ?? throw new TransmitlyException(0, "invalid_response", "Empty accepted response");
    }

    public Task<JsonElement> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/emails/" + Path(id), cancellationToken: cancellationToken);
}

public sealed class MessagesResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> ListAsync(
        string? status = null,
        string? stream = null,
        string? q = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default) =>
        Client.RequestAsync(
            HttpMethod.Get,
            "/v1/messages",
            query: new Dictionary<string, string?>
            {
                ["status"] = status,
                ["stream"] = stream,
                ["q"] = q,
                ["limit"] = limit?.ToString(),
                ["offset"] = offset?.ToString(),
            },
            cancellationToken: cancellationToken);

    public Task<JsonElement> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/messages/" + Path(id), cancellationToken: cancellationToken);

    public Task<JsonElement> DeliveriesAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/messages/" + Path(id) + "/deliveries", cancellationToken: cancellationToken);
}

public sealed class DomainsResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/domains", cancellationToken: cancellationToken);

    public Task<JsonElement> CreateAsync(string domain, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(
            HttpMethod.Post,
            "/v1/domains",
            new Dictionary<string, string> { ["domain"] = domain },
            cancellationToken: cancellationToken);

    public Task<JsonElement> VerifyAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(
            HttpMethod.Post,
            "/v1/domains/" + Path(id) + "/verify",
            new Dictionary<string, string>(),
            cancellationToken: cancellationToken);

    public Task<JsonElement> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Delete, "/v1/domains/" + Path(id), cancellationToken: cancellationToken);
}

public sealed class SuppressionsResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> ListAsync(
        string? status = null,
        string? q = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        Client.RequestAsync(
            HttpMethod.Get,
            "/v1/suppressions",
            query: new Dictionary<string, string?>
            {
                ["status"] = status,
                ["q"] = q,
                ["limit"] = limit?.ToString(),
            },
            cancellationToken: cancellationToken);

    public Task<JsonElement> AddAsync(SuppressionInput input, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Post, "/v1/suppressions", input, cancellationToken: cancellationToken);

    public Task<JsonElement> RemoveAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Delete, "/v1/suppressions/" + Path(id), cancellationToken: cancellationToken);
}

public sealed class WebhooksResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/webhooks", cancellationToken: cancellationToken);

    public Task<JsonElement> CreateAsync(WebhookInput input, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Post, "/v1/webhooks", input, cancellationToken: cancellationToken);

    public Task<JsonElement> RemoveAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Delete, "/v1/webhooks/" + Path(id), cancellationToken: cancellationToken);
}

public sealed class InboundResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> RoutesAsync(CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/inbound/routes", cancellationToken: cancellationToken);

    public Task<JsonElement> AddRouteAsync(InboundRouteInput input, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Post, "/v1/inbound/routes", input, cancellationToken: cancellationToken);

    public Task<JsonElement> RemoveRouteAsync(string id, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Delete, "/v1/inbound/routes/" + Path(id), cancellationToken: cancellationToken);
}

public sealed class UsageResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> GetAsync(CancellationToken cancellationToken = default) =>
        Client.RequestAsync(HttpMethod.Get, "/v1/usage", cancellationToken: cancellationToken);
}

public sealed class EventsResource(TransmitlyClient client) : Resource(client)
{
    public Task<JsonElement> ListAsync(int? limit = null, CancellationToken cancellationToken = default) =>
        Client.RequestAsync(
            HttpMethod.Get,
            "/v1/events",
            query: new Dictionary<string, string?> { ["limit"] = limit?.ToString() },
            cancellationToken: cancellationToken);
}
