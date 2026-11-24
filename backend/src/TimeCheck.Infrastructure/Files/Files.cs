using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using SkiaSharp;
using TimeCheck.Application.Abstractions;
using TimeCheck.Domain.Common;

namespace TimeCheck.Infrastructure.Files;

/// <summary>
/// Supabase Storage over its REST API with the secret key (backend only, §8.1). Buckets are private; the browser gets signed URLs.
/// </summary>
public sealed class SupabaseStorage(HttpClient http) : IFileStorage
{
    public async Task UploadAsync(string bucket, string path, byte[] content, string contentType, CancellationToken ct)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"storage/v1/object/{bucket}/{Escape(path)}") { Content = body };
        req.Headers.Add("x-upsert", "true"); // the key is the content hash, so a retry writes the same bytes
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> DownloadAsync(string bucket, string path, CancellationToken ct)
    {
        using var res = await http.GetAsync($"storage/v1/object/{bucket}/{Escape(path)}", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<string> SignedUrlAsync(string bucket, string path, TimeSpan ttl, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync($"storage/v1/object/sign/{bucket}/{Escape(path)}", new { expiresIn = (int)ttl.TotalSeconds }, ct);
        res.EnsureSuccessStatusCode();
        var signed = await res.Content.ReadFromJsonAsync<Signed>(ct) ?? throw new InvalidOperationException("Storage returned no signed URL.");
        return new Uri(http.BaseAddress!, $"storage/v1{signed.SignedURL}").ToString();
    }

    public async Task DeleteAsync(string bucket, string path, CancellationToken ct)
    {
        using var res = await http.DeleteAsync($"storage/v1/object/{bucket}/{Escape(path)}", ct);
        if (res.StatusCode != HttpStatusCode.NotFound) res.EnsureSuccessStatusCode();
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private sealed record Signed(string SignedURL);
}

/// <summary>
/// Decodes by content (Skia sniffs the magic bytes, never the name), keeps JPEG/PNG/WebP only, turns the picture upright, caps
/// the long side and re-encodes as JPEG. Re-encoding drops EXIF/GPS and anything appended to the file (§6.3, §8.3).
/// HEIC from iPhones is converted to JPEG in the browser before upload (§11.3).
/// </summary>
public sealed class SkiaImageSanitizer : IImageSanitizer
{
    public const int MaxSide = 2048;
    public const int Quality = 85;
    private static readonly SKEncodedImageFormat[] Allowed = [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    public CleanImage Sanitize(byte[] input)
    {
        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null || !Allowed.Contains(codec.EncodedFormat) || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > 50_000_000) // decompression-bomb guard
            throw new DomainException("upload.image_invalid", "Upload a JPEG, PNG or WebP photo.");

        using var decoded = SKBitmap.Decode(codec) ?? throw new DomainException("upload.image_invalid", "That image couldn't be read.");
        using var upright = Orient(decoded, codec.EncodedOrigin);
        var scale = Math.Min(1f, (float)MaxSide / Math.Max(upright.Width, upright.Height));
        using var sized = scale < 1f
            ? upright.Resize(new SKImageInfo((int)(upright.Width * scale), (int)(upright.Height * scale)), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : upright.Copy();
        using var image = SKImage.FromBitmap(sized);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, Quality);
        return new CleanImage(jpeg.ToArray(), "image/jpeg");
    }

    // shortcut: handles the rotations phones write (90/180/270); mirrored EXIF origins are kept as stored, which is rare in practice.
    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        var degrees = origin switch { SKEncodedOrigin.BottomRight => 180, SKEncodedOrigin.RightTop => 90, SKEncodedOrigin.LeftBottom => 270, _ => 0 };
        if (degrees == 0) return src.Copy();
        var swap = degrees != 180;
        var dst = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);
        using var canvas = new SKCanvas(dst);
        canvas.Translate(dst.Width / 2f, dst.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-src.Width / 2f, -src.Height / 2f);
        using var img = SKImage.FromBitmap(src);
        canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return dst;
    }
}

/// <summary>Detection switched off: devotions are marked SKIPPED. The default until a provider is chosen (§15 Q6, ADR 0006).</summary>
public sealed class DisabledAiContentDetector : IAiContentDetector
{
    public bool Enabled => false;
    public Task<AiScore?> ScoreTextAsync(string text, CancellationToken ct) => Task.FromResult<AiScore?>(null);
    public Task<AiScore?> ScoreImageAsync(byte[] image, string contentType, CancellationToken ct) => Task.FromResult<AiScore?>(null);
}

/// <summary>
/// Development only (Ai:Provider = "fake"): a made-up score derived from the content hash, so the pipeline and every band can be
/// seen without sending anything anywhere. It knows nothing about AI; production refuses to start with it.
/// </summary>
public sealed class FakeAiContentDetector : IAiContentDetector
{
    public const string Provider = "fake";
    public bool Enabled => true;
    public Task<AiScore?> ScoreTextAsync(string text, CancellationToken ct) => Task.FromResult<AiScore?>(Score(System.Text.Encoding.UTF8.GetBytes(text)));
    public Task<AiScore?> ScoreImageAsync(byte[] image, string contentType, CancellationToken ct) => Task.FromResult<AiScore?>(Score(image));

    private static AiScore Score(byte[] content) => new(Math.Round(SHA256.HashData(content)[0] * 100m / 255m, 2), Provider, "dev-1");
}
