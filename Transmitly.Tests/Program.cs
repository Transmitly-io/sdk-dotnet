using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Transmitly;

var failures = 0;
var assertions = 0;

void Check(bool condition, string message)
{
    assertions++;
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {message}");
    }
}

// Webhook verification
var secret = "whsec_test";
var body = "{\"type\":\"email.delivered_to_mx\"}";
var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var signature = Convert.ToHexString(
    HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")))
    .ToLowerInvariant();
var header = $"t={timestamp},v1={signature}";
Check(TransmitlyClient.VerifyWebhook(body, header, secret), "valid webhook signature");
Check(!TransmitlyClient.VerifyWebhook(body, header, "wrong"), "wrong webhook secret");
Check(!TransmitlyClient.VerifyWebhook(body, $"t={timestamp - 400},v1={signature}", secret), "stale webhook timestamp");
Check(!TransmitlyClient.VerifyWebhook(body, "garbage", secret), "malformed webhook header");

// Validation happens before the request
var empty = new TransmitlyClient("txk_test", httpClient: new HttpClient(new StubHandler(_ => Json(HttpStatusCode.Accepted, "{}"))));
try
{
    await empty.Emails.SendAsync(new EmailMessage { From = "a@acme.com", To = Array.Empty<string>(), Subject = "s", Text = "t" });
    Check(false, "empty to is rejected");
}
catch (ArgumentException)
{
    Check(true, "empty to is rejected");
}
try
{
    await empty.Emails.SendAsync(new EmailMessage { From = "a@acme.com", To = new[] { "b@example.net" }, Subject = "s" });
    Check(false, "missing body is rejected");
}
catch (ArgumentException)
{
    Check(true, "missing body is rejected");
}

// Send request shape
var capture = new RequestCapture();
var handler = new StubHandler(request =>
{
    capture.Capture(request);
    return Json(HttpStatusCode.Accepted, "{\"id\":\"msg_123\",\"status\":\"accepted\",\"recipients\":1}");
});
var tx = new TransmitlyClient("txk_test", "http://localhost:9999", httpClient: new HttpClient(handler));
var accepted = await tx.Emails.SendAsync(
    new EmailMessage
    {
        From = "Notify <notify@acme.com>",
        To = new[] { "ada@example.net" },
        Subject = "Welcome",
        Html = "<p>Hi</p>",
        Tags = new Dictionary<string, string> { ["flow"] = "onboarding" },
    },
    idempotencyKey: "welcome-1");

Check(accepted.Id == "msg_123", "accepted id");
Check(capture.Method == HttpMethod.Post, "send method");
Check(capture.Path == "/v1/emails", "send path");
Check(capture.Authorization == "Bearer txk_test", "authorization header");
Check(capture.IdempotencyKey == "welcome-1", "idempotency header");
using var payload = JsonDocument.Parse(capture.Body!);
Check(payload.RootElement.GetProperty("from").GetString() == "Notify <notify@acme.com>", "from payload");
Check(payload.RootElement.GetProperty("to")[0].GetString() == "ada@example.net", "to payload");
Check(payload.RootElement.GetProperty("tags").GetProperty("flow").GetString() == "onboarding", "tags payload");

// Error mapping
var conflict = new TransmitlyClient(
    "txk_test",
    "http://localhost:9999",
    httpClient: new HttpClient(new StubHandler(_ => Json(
        HttpStatusCode.Conflict,
        "{\"error\":{\"code\":\"idempotency_conflict\",\"message\":\"Already used\"}}"))));
try
{
    await conflict.Emails.SendAsync(new EmailMessage
    {
        From = "a@acme.com",
        To = new[] { "b@example.net" },
        Subject = "s",
        Text = "t",
    });
    Check(false, "conflict should throw");
}
catch (TransmitlyException error)
{
    Check(error.Status == 409, "error status");
    Check(error.Code == "idempotency_conflict", "error code");
    Check(error.Message == "Already used", "error message");
}

// Query serialization
capture = new RequestCapture();
var listClient = new TransmitlyClient(
    "txk_test",
    "http://localhost:9999",
    httpClient: new HttpClient(new StubHandler(request =>
    {
        capture.Capture(request);
        return Json(HttpStatusCode.OK, "{\"messages\":[]}");
    })));
await listClient.Messages.ListAsync(status: "bounced", limit: 20);
Check(capture.Query.Contains("status=bounced"), "query status");
Check(capture.Query.Contains("limit=20"), "query limit");

// Optional live smoke test; skipped unless TRANSMITLY_API_KEY is set.
var liveKey = Environment.GetEnvironmentVariable("TRANSMITLY_API_KEY");
if (!string.IsNullOrWhiteSpace(liveKey))
{
    using var live = new TransmitlyClient(
        liveKey,
        Environment.GetEnvironmentVariable("TRANSMITLY_BASE_URL") ?? "https://transmitly.io");
    var liveAccepted = await live.Emails.SendAsync(new EmailMessage
    {
        From = "Notify <notify@transmitly.io>",
        To = new[] { "sdk-dotnet@transmitly.test" },
        Subject = "Dotnet SDK smoke",
        Text = "hello",
        TestMode = true,
    });
    Check(liveAccepted.Status == "accepted", "live accepted status");
    Check(liveAccepted.TestMode == true, "live test mode");
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} failure(s), {assertions} assertion(s)");
    return 1;
}
Console.WriteLine($"OK — {assertions} assertions");
return 0;

static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
{
    Content = new StringContent(body, Encoding.UTF8, "application/json"),
};

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(respond(request));
}

sealed class RequestCapture
{
    public HttpMethod? Method { get; private set; }
    public string Path { get; private set; } = "";
    public string Query { get; private set; } = "";
    public string? Authorization { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? Body { get; private set; }

    public void Capture(HttpRequestMessage request)
    {
        Method = request.Method;
        Path = request.RequestUri!.AbsolutePath;
        Query = request.RequestUri.Query;
        Authorization = request.Headers.Authorization?.ToString();
        IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.FirstOrDefault() : null;
        Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}
