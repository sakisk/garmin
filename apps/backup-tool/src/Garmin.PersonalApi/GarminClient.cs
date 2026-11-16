using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Garmin.PersonalApi;

/// <summary>Reads Garmin Personal API data using caller-owned HTTP and credentials.</summary>
/// <remarks>
/// The caller owns HttpClient and should disable automatic redirects. Tokens are kept only in memory.
/// Use one instance per key and do not invoke it concurrently. No environment or filesystem access occurs.
/// </remarks>
public sealed class GarminClient
{
    public static Uri DefaultBaseAddress { get; } = new("https://personal.connectapi.garmin.com/");
    private readonly HttpClient http;
    private readonly string key;
    private readonly Uri address;
    private string? token;
    private DateTimeOffset tokenExpiry;

    public GarminClient(HttpClient httpClient, string personalApiKey, Uri? baseAddress = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(personalApiKey) || personalApiKey.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("A nonempty Personal API key without whitespace is required.", nameof(personalApiKey));
        address = baseAddress ?? DefaultBaseAddress;
        if (!address.IsAbsoluteUri || (address.Scheme != "https" && !(address.Scheme == "http" && address.IsLoopback))
            || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0
            || address.AbsolutePath != "/")
            throw new ArgumentException("The API base address must be an HTTPS origin (HTTP is allowed on loopback for tests).", nameof(baseAddress));
        http = httpClient;
        key = personalApiKey;
    }

    public async Task<Activity> GetActivityAsync(long activityId, CancellationToken cancellationToken = default)
    {
        if (activityId <= 0) throw new ArgumentOutOfRangeException(nameof(activityId), "Activity ID must be positive.");
        // Cover body reads as well as headers; HttpClient.Timeout alone ends at headers with ResponseHeadersRead.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(http.Timeout);
        var ct = budget.Token;
        try
        {
            if (token is null || tokenExpiry <= DateTimeOffset.UtcNow.AddSeconds(30))
                await AuthenticateAsync(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(address, "activity-service/activity/" + activityId.ToString(CultureInfo.InvariantCulture)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            CheckStatus(response.StatusCode, authentication: false);
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(body, ApiJsonContext.Default.Activity, ct).ConfigureAwait(false)
                ?? throw new GarminApiException(GarminFailure.InvalidResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GarminApiException(GarminFailure.Timeout);
        }
        catch (HttpRequestException) { throw new GarminApiException(GarminFailure.Connection); }
        catch (IOException) { throw new GarminApiException(GarminFailure.Connection); }
        catch (JsonException) { throw new GarminApiException(GarminFailure.InvalidResponse); }
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(address, "personalapitoken-service/personal-api/personal-api-key/token"));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        CheckStatus(response.StatusCode, authentication: true);
        // JWTs are ASCII. Do not let an untrusted charset header select an encoding or escape sanitization.
        var candidate = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)).Trim();
        try
        {
            var parts = candidate.Split('.');
            if (parts.Length != 3 || parts.Any(p => p.Length == 0)
                || candidate.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
                throw new FormatException();
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var claims = JsonSerializer.Deserialize(Convert.FromBase64String(payload), ApiJsonContext.Default.TokenClaims);
            if (claims?.ExpiresAt is not { } expires) throw new FormatException();
            var expiry = DateTimeOffset.FromUnixTimeSeconds(expires);
            if (expiry <= DateTimeOffset.UtcNow) throw new FormatException();
            tokenExpiry = expiry;
            token = candidate;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            throw new GarminApiException(GarminFailure.InvalidResponse);
        }
    }

    private void CheckStatus(HttpStatusCode status, bool authentication)
    {
        if ((int)status is >= 200 and < 300) return;
        var failure = status switch
        {
            HttpStatusCode.Unauthorized => GarminFailure.Authentication,
            HttpStatusCode.Forbidden when authentication => GarminFailure.Authentication,
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound when !authentication => GarminFailure.ActivityUnavailable,
            HttpStatusCode.BadRequest when !authentication => GarminFailure.InvalidActivity,
            HttpStatusCode.TooManyRequests => GarminFailure.RateLimited,
            _ => GarminFailure.Service
        };
        if (failure == GarminFailure.Authentication) token = null;
        throw new GarminApiException(failure, status);
    }
}
