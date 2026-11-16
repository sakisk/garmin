using System.Globalization;
using Garmin.PersonalApi;

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("""
        Usage: Garmin.Cli <activity-id>

        Retrieve one activity using Garmin Connect Personal API.
        Create a key in Garmin Connect web: Profile & Account > Settings > Personal API Key.
        https://connect.garmin.com/app/settings/personalApiToken
        Configure GARMIN_PERSONAL_API_KEY in the environment. Never pass the key as an argument.
        In zsh, enter it without recording it in shell history:
          read -rs 'GARMIN_PERSONAL_API_KEY?Personal API key: '; echo
          export GARMIN_PERSONAL_API_KEY
          Garmin.Cli 123456789
          unset GARMIN_PERSONAL_API_KEY

        Open an activity in Garmin Connect web and copy its numeric ID from the URL:
          https://connect.garmin.com/modern/activity/123456789

        Distance is in metres (m); duration is in seconds (s). Start uses UTC when available;
        otherwise activity-local time with unknown UTC offset. Missing values say unavailable.
        Exit codes: 0 success/help, 2 invalid input/configuration, 1 API/network failure, 130 cancelled.
        """);
    return 0;
}

if (args.Length != 1 || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
    return Fail("Provide an activity ID as a positive integer. Use --help for setup.", 2);
var key = Environment.GetEnvironmentVariable("GARMIN_PERSONAL_API_KEY");
if (string.IsNullOrWhiteSpace(key) || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
    return Fail("Set GARMIN_PERSONAL_API_KEY to a valid Personal API key. Use --help for setup.", 2);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var endpoint = Environment.GetEnvironmentVariable("GARMIN_PERSONAL_API_BASE_URL");
    if (endpoint is not null && !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        return Fail("Invalid GARMIN_PERSONAL_API_BASE_URL. Use an HTTPS origin, or HTTP loopback for testing.", 2);
    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    var client = new GarminClient(http, key, endpoint is null ? null : new Uri(endpoint));
    var activity = await client.GetActivityAsync(id, cancellation.Token);
    Console.WriteLine($"Activity ID: {activity.ActivityId?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
    Console.WriteLine($"Name: {Text(activity.ActivityName, key)}");
    Console.WriteLine($"Type: {Text(activity.ActivityType?.TypeKey, key)}");
    Console.WriteLine($"Start: {Start(activity.Summary)}");
    Console.WriteLine($"Distance: {Number(activity.Summary?.Distance, "m")}");
    Console.WriteLine($"Duration: {Number(activity.Summary?.Duration, "s")}");
    return 0;
}
catch (GarminApiException ex) { return Fail(ex.Message, 1); }
catch (ArgumentException) { return Fail("Invalid GARMIN_PERSONAL_API_BASE_URL configuration.", 2); }
catch (OperationCanceledException) { return Fail("Request cancelled.", 130); }

static int Fail(string message, int code) { Console.Error.WriteLine(message); return code; }

static string Text(string? value, string key) => string.IsNullOrWhiteSpace(value) ? "unavailable"
    : new string(value.Replace(key, "[redacted]", StringComparison.Ordinal).Select(c => char.IsControl(c) ? ' ' : c).ToArray());

static string Start(ActivitySummary? summary) => summary?.StartTimeGmt is { } utc
    ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + " (UTC)"
    : summary?.StartTimeLocal is { } local
        ? local.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture) + " (activity local time; UTC offset unknown)"
        : "unavailable";

static string Number(double? value, string unit) => value is { } number
    ? number.ToString("G", CultureInfo.InvariantCulture) + " " + unit : "unavailable";
