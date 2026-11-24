using System.Net;
using System.Net.Http.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using TimeCheck.Application.Abstractions;

namespace TimeCheck.Infrastructure.Notifications;

/// <summary>No email provider configured (ADR 0007): notifications stay in-app and push.</summary>
public sealed class DisabledEmailSender(string appUrl) : IEmailSender
{
    public bool Enabled => false;
    public string AppUrl => appUrl;
    public Task SendAsync(EmailMessage message, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Development only: hands mail to the local Supabase Mailpit over its HTTP API (no SMTP port needed). Nothing leaves the
/// machine; read it at the Mailpit web UI.
/// </summary>
public sealed class MailpitEmailSender(HttpClient http, string appUrl, string from) : IEmailSender
{
    public bool Enabled => true;
    public string AppUrl => appUrl;

    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("api/v1/send", new
        {
            From = new { Email = from, Name = "TimeCheck" },
            To = new[] { new { Email = m.To } },
            m.Subject,
            m.Text,
            HTML = m.Html,
        }, ct);
        res.EnsureSuccessStatusCode();
    }
}

public sealed class DisabledPushSender : IPushSender
{
    public bool Enabled => false;
    public string? PublicKey => null;
    public Task<PushOutcome> SendAsync(string endpoint, string p256dh, string auth, string payloadJson, CancellationToken ct) =>
        Task.FromResult(PushOutcome.Failed);
}

/// <summary>Web push with VAPID keys (Notifications:Push). The private key is a secret: user-secrets locally, env in production.</summary>
public sealed class WebPushSender(HttpClient http, string publicKey, string privateKey, string subject) : IPushSender
{
    private readonly PushServiceClient _client = new(http)
    {
        DefaultAuthentication = new VapidAuthentication(publicKey, privateKey) { Subject = subject },
        DefaultTimeToLive = (int)TimeSpan.FromHours(12).TotalSeconds,
    };

    public bool Enabled => true;
    public string? PublicKey => publicKey;

    public async Task<PushOutcome> SendAsync(string endpoint, string p256dh, string auth, string payloadJson, CancellationToken ct)
    {
        var sub = new PushSubscription { Endpoint = endpoint, Keys = new Dictionary<string, string> { ["p256dh"] = p256dh, ["auth"] = auth } };
        try
        {
            await _client.RequestPushMessageDeliveryAsync(sub, new PushMessage(payloadJson), ct);
            return PushOutcome.Sent;
        }
        catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushOutcome.Gone;
        }
        catch (Exception e) when (e is PushServiceClientException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return PushOutcome.Failed; // best effort: in-app already has it
        }
    }
}
