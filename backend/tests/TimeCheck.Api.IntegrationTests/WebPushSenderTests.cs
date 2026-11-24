using System.Net;
using System.Security.Cryptography;
using AwesomeAssertions;
using TimeCheck.Application.Abstractions;
using TimeCheck.Infrastructure.Notifications;

namespace TimeCheck.Api.IntegrationTests;

/// <summary>The real push sender, against a stub push service: the request is VAPID-signed and encrypted, and 410 means gone.</summary>
public class WebPushSenderTests
{
    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A P-256 key pair as base64url (uncompressed public point, private scalar), like a browser or our VAPID keys.</summary>
    private static (string Public, string Private) KeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);
        return (B64Url([0x04, .. p.Q.X!, .. p.Q.Y!]), B64Url(p.D!));
    }

    private sealed class Stub(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    [Fact]
    public async Task Sends_an_encrypted_vapid_signed_message_and_reports_expired_subscriptions()
    {
        var vapid = KeyPair();
        var browser = KeyPair();
        var auth = B64Url(RandomNumberGenerator.GetBytes(16));

        var ok = new Stub(HttpStatusCode.Created);
        using (var http = new HttpClient(ok))
        {
            var sender = new WebPushSender(http, vapid.Public, vapid.Private, "mailto:no-reply@timecheck.test");
            (await sender.SendAsync("https://push.test/abc", browser.Public, auth, """{"title":"Hi"}""", CancellationToken.None))
                .Should().Be(PushOutcome.Sent);
        }
        ok.Request!.RequestUri!.ToString().Should().Be("https://push.test/abc");
        ok.Request.Headers.Authorization!.ToString().Should().StartWith("vapid t=").And.Contain($"k={vapid.Public}");
        ok.Request.Content!.Headers.ContentEncoding.Should().Contain("aes128gcm");
        ok.Body.Length.Should().BeGreaterThan(86, "an aes128gcm header plus ciphertext");
        System.Text.Encoding.UTF8.GetString(ok.Body).Should().NotContain("Hi", "the payload is encrypted");

        using var goneHttp = new HttpClient(new Stub(HttpStatusCode.Gone));
        (await new WebPushSender(goneHttp, vapid.Public, vapid.Private, "mailto:x@timecheck.test")
            .SendAsync("https://push.test/old", browser.Public, auth, "{}", CancellationToken.None)).Should().Be(PushOutcome.Gone);
    }
}
