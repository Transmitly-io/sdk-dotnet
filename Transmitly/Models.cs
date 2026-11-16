using System.Text.Json;
using System.Text.Json.Serialization;

namespace Transmitly;

/// <summary>Raised for a non-2xx API response or a transport failure.</summary>
public sealed class TransmitlyException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public JsonElement? Details { get; }
    public string? Payload { get; }

    public TransmitlyException(int status, string code, string message, JsonElement? details = null, string? payload = null)
        : base(string.IsNullOrWhiteSpace(message) ? $"Request failed ({status})" : message)
    {
        Status = status;
        Code = code;
        Details = details;
        Payload = payload;
    }
}

/// <summary>A file attached to a message. Use base64 <see cref="Content"/> or a public HTTPS <see cref="Path"/>.</summary>
public sealed class Attachment
{
    public required string Filename { get; init; }
    public string? Content { get; init; }
    public string? Path { get; init; }
    public string? ContentType { get; init; }
    public string? ContentId { get; init; }

    public static Attachment FromFile(string path, string? filename = null, string? contentType = null) => new()
    {
        Filename = filename ?? System.IO.Path.GetFileName(path),
        Content = Convert.ToBase64String(File.ReadAllBytes(path)),
        ContentType = contentType ?? "application/octet-stream",
    };
}

/// <summary>The accepted message returned by <c>POST /v1/emails</c>.</summary>
public sealed class EmailAccepted
{
    public string Id { get; init; } = "";
    public string Status { get; init; } = "";
    public int Recipients { get; init; }
    public string? Warning { get; init; }
    public bool? TestMode { get; init; }
}

/// <summary>The message payload for <c>POST /v1/emails</c>.</summary>
public sealed class EmailMessage
{
    public required string From { get; init; }
    public required IReadOnlyList<string> To { get; init; }
    public required string Subject { get; init; }
    public string? Html { get; init; }
    public string? Text { get; init; }
    public string? ReplyTo { get; init; }
    public string? Stream { get; init; }
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public string? TemplateId { get; init; }
    public IReadOnlyDictionary<string, string>? TemplateData { get; init; }
    public bool? TestMode { get; init; }
    public IReadOnlyList<string>? Cc { get; init; }
    public IReadOnlyList<string>? Bcc { get; init; }
    public string? ScheduledAt { get; init; }
    public string? Topic { get; init; }
    public IReadOnlyList<Attachment>? Attachments { get; init; }
}

/// <summary>A suppression entry.</summary>
public sealed class SuppressionInput
{
    public required string Email { get; init; }
    public string? Reason { get; init; }
    public string? Stream { get; init; }
    public string? Note { get; init; }
}

/// <summary>A webhook endpoint.</summary>
public sealed class WebhookInput
{
    public required string Url { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? DomainIds { get; init; }
}

/// <summary>An inbound route.</summary>
public sealed class InboundRouteInput
{
    public required string Address { get; init; }
    public required string Action { get; init; }
    public string? Destination { get; init; }
    public string? Note { get; init; }
}

/// <summary>A contact sourced from the API.</summary>
public sealed class ContactInput
{
    public required string Email { get; init; }
    public string? Name { get; init; }
    public string? AudienceId { get; init; }
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
