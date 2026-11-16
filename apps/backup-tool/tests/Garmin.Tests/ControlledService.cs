using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Garmin.Tests;

internal sealed class ControlledService : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly Task serving;
    public ConcurrentQueue<(string Method, string Path, string? Authorization)> Requests { get; } = new();
    public Uri Address { get; }
    public static string Token { get; } = MakeToken(DateTimeOffset.UtcNow.AddHours(1));
    public const string TokenPath = "/personalapitoken-service/personal-api/personal-api-key/token";
    public const string ActivityJson = """
        {"activityId":42,"activityName":"Synthetic run","activityTypeDTO":{"typeId":1,"typeKey":"running"},
         "summaryDTO":{"startTimeGMT":"2026-01-02T09:30:00.000","startTimeLocal":"2026-01-02T10:30:00.000",
                       "distance":5000.25,"duration":1800.5}}
        """;

    public ControlledService(Func<string, (int Status, string Body)>? respond = null, TimeSpan? bodyDelay = null,
        string tokenContentType = "text/plain")
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/");
        port.Stop();
        listener.Prefixes.Add(Address.AbsoluteUri);
        listener.Start();
        serving = ServeAsync(respond ?? (path => (200, path == TokenPath ? Token : ActivityJson)), bodyDelay, tokenContentType);
    }

    public static string MakeToken(DateTimeOffset expiry) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"exp\":{expiry.ToUnixTimeSeconds()}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".synthetic-signature";

    private async Task ServeAsync(Func<string, (int Status, string Body)> respond, TimeSpan? bodyDelay, string tokenContentType)
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                Requests.Enqueue((request.HttpMethod, request.RawUrl!, request.Headers["Authorization"]));
                var reply = respond(request.RawUrl!);
                context.Response.StatusCode = reply.Status;
                if (reply.Status == 302) context.Response.RedirectLocation = reply.Body;
                context.Response.ContentType = request.RawUrl == TokenPath ? tokenContentType : "application/json";
                var bytes = Encoding.UTF8.GetBytes(reply.Body);
                context.Response.ContentLength64 = bytes.Length;
                if (bodyDelay is { } delay && request.RawUrl != TokenPath)
                {
                    await context.Response.OutputStream.WriteAsync(bytes.AsMemory(0, 1));
                    await context.Response.OutputStream.FlushAsync();
                    await Task.Delay(delay);
                    bytes = bytes[1..];
                }
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        listener.Close();
        await serving;
    }
}
