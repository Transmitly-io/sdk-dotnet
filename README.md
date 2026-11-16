# Transmitly .NET SDK

Dependency-free .NET 8 client for the Transmitly API.

```bash
# from the platform repository
dotnet add reference sdk/dotnet/Transmitly/Transmitly.csproj
```

```csharp
using Transmitly;

var tx = new TransmitlyClient(Environment.GetEnvironmentVariable("TRANSMITLY_API_KEY")!);

var accepted = await tx.Emails.SendAsync(new EmailMessage
{
    From = "Notify <notify@mail.acme.com>",
    To = new[] { "ada@example.net" },
    Subject = "Your workspace is ready",
    Html = "<p>Welcome.</p>",
    ReplyTo = "Support <support@acme.com>",
    Tags = new Dictionary<string, string> { ["flow"] = "onboarding" },
});
// accepted.Id, accepted.Recipients
```

`SendAsync` generates an idempotency key. Pass `idempotencyKey:` to control it
yourself; retries with the same key return the original message.

## ASP.NET Core

Register one client and inject it into controllers, minimal APIs or workers:

```csharp
// Program.cs
builder.Services.AddSingleton(_ =>
    new TransmitlyClient(builder.Configuration["Transmitly:ApiKey"]
        ?? throw new InvalidOperationException("Transmitly:ApiKey is required")));
```

```csharp
app.MapPost("/welcome", async (TransmitlyClient tx, string email) =>
{
    var accepted = await tx.Emails.SendAsync(new EmailMessage
    {
        From = "Notify <notify@mail.acme.com>",
        To = new[] { email },
        Subject = "Welcome to Acme",
        Html = "<p>Your workspace is ready.</p>",
    });
    return Results.Accepted($"/messages/{accepted.Id}", new { accepted.Id });
});
```

Keep the API key in configuration or a secret store. Never ship it to a client
application.

## Resources

```csharp
await tx.Emails.GetAsync(messageId);
await tx.Messages.ListAsync(status: "bounced", limit: 20);
await tx.Messages.DeliveriesAsync(messageId);
await tx.Domains.ListAsync();
await tx.Domains.CreateAsync("mail.acme.com");
await tx.Domains.VerifyAsync(domainId);
await tx.Suppressions.ListAsync();
await tx.Suppressions.AddAsync(new SuppressionInput { Email = "bounce@example.net", Reason = "manual" });
await tx.Webhooks.ListAsync();
await tx.Webhooks.CreateAsync(new WebhookInput
{
    Url = "https://api.acme.com/hooks",
    Events = new[] { "email.delivered_to_mx" },
});
await tx.Inbound.RoutesAsync();
await tx.Usage.GetAsync();
await tx.Events.ListAsync(limit: 50);
```

Resources other than `Emails.SendAsync` return `JsonElement` so new fields do
not require an SDK release:

```csharp
var messages = await tx.Messages.ListAsync(status: "bounced");
foreach (var message in messages.GetProperty("messages").EnumerateArray())
{
    Console.WriteLine(message.GetProperty("id").GetString());
}
```

## Attachments

```csharp
await tx.Emails.SendAsync(new EmailMessage
{
    From = "Notify <notify@mail.acme.com>",
    To = new[] { "ada@example.net" },
    Subject = "November invoice",
    Html = "<p>Attached.</p>",
    Attachments = new[] { Attachment.FromFile("invoice.pdf", contentType: "application/pdf") },
});
```

## Webhooks

Verify the raw request body before deserializing it:

```csharp
app.MapPost("/webhooks/transmitly", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var raw = await reader.ReadToEndAsync();
    var signature = request.Headers["Transmitly-Signature"].ToString();
    var secret = builder.Configuration["Transmitly:WebhookSecret"]!;

    if (!TransmitlyClient.VerifyWebhook(raw, signature, secret))
    {
        return Results.BadRequest();
    }

    // Deserialize and handle only after the signature passes.
    return Results.Ok();
});
```

Non-2xx responses throw `TransmitlyException` with `Status`, `Code`, `Details`
and `Payload`. Domain, webhook and inbound management require an API key with
the `admin` scope.

```bash
dotnet pack Transmitly/Transmitly.csproj -c Release
```
