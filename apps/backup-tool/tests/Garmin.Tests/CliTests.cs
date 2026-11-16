using System.Diagnostics;

namespace Garmin.Tests;

public sealed class CliTests
{
    private const string Key = "synthetic-test-key";

    [Fact]
    public async Task Authenticates_and_prints_one_activity()
    {
        await using var service = new ControlledService();
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Contains("Activity ID: 42", result.Output);
        Assert.Contains("Name: Synthetic run", result.Output);
        Assert.Contains("Type: running", result.Output);
        Assert.Contains("Start: 2026-01-02T09:30:00.000Z (UTC)", result.Output);
        Assert.Contains("Distance: 5000.25 m", result.Output);
        Assert.Contains("Duration: 1800.5 s", result.Output);
        Assert.Equal(new[] {
            ("POST", ControlledService.TokenPath, (string?)$"ApiKey {Key}"),
            ("GET", "/activity-service/activity/42", (string?)$"Bearer {ControlledService.Token}")
        }, service.Requests.ToArray());
        AssertSanitized(result);
    }

    [Theory]
    [InlineData("{\"activityId\":42}", "Distance: unavailable", "Duration: unavailable")]
    [InlineData("{\"activityId\":42,\"summaryDTO\":null,\"activityTypeDTO\":null}", "Distance: unavailable", "Duration: unavailable")]
    [InlineData("{\"activityId\":42,\"summaryDTO\":{\"distance\":0,\"duration\":0}}", "Distance: 0 m", "Duration: 0 s")]
    public async Task Missing_values_remain_distinct_from_zero(string json, string distance, string duration)
    {
        await using var service = new ControlledService(path => (200, path == ControlledService.TokenPath ? ControlledService.Token : json));
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Name: unavailable", result.Output);
        Assert.Contains("Type: unavailable", result.Output);
        Assert.Contains("Start: unavailable", result.Output);
        Assert.Contains(distance, result.Output);
        Assert.Contains(duration, result.Output);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("secret-in-an-invalid-argument")]
    public async Task Invalid_ids_fail_without_http_or_echoing_arguments(string argument)
    {
        await using var service = new ControlledService();
        var result = await RunAsync(service, Key, argument);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("positive integer", result.Error);
        Assert.DoesNotContain(argument, result.Error);
        Assert.Empty(service.Requests);
        AssertSanitized(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("bad\nkey")]
    public async Task Missing_or_malformed_credentials_fail_locally(string? key)
    {
        await using var service = new ControlledService();
        var result = await RunAsync(service, key, "42");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("GARMIN_PERSONAL_API_KEY", result.Error);
        Assert.Empty(service.Requests);
        AssertSanitized(result);
    }

    [Theory]
    [InlineData(true, 401, "Authentication rejected")]
    [InlineData(true, 403, "Authentication rejected")]
    [InlineData(false, 401, "Authentication rejected")]
    [InlineData(false, 403, "Activity unavailable")]
    [InlineData(false, 404, "Activity unavailable")]
    [InlineData(false, 400, "Invalid activity ID")]
    [InlineData(true, 500, "Garmin service failed")]
    [InlineData(false, 503, "Garmin service failed")]
    [InlineData(false, 429, "rate limit")]
    public async Task Http_failures_have_sanitized_actionable_errors(bool authentication, int status, string message)
    {
        await using var service = new ControlledService(path =>
            (path == ControlledService.TokenPath) == authentication
                ? (status, $"{Key} {ControlledService.Token} private-response-body")
                : (200, path == ControlledService.TokenPath ? ControlledService.Token : ControlledService.ActivityJson));
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(message, result.Error);
        Assert.DoesNotContain("private-response-body", result.Error);
        Assert.DoesNotContain("Exception", result.Error);
        Assert.Equal(authentication ? 1 : 2, service.Requests.Count);
        AssertSanitized(result);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(true, "not-a-token")]
    [InlineData(true, "e30.e30.signature")]
    [InlineData(false, "null")]
    [InlineData(false, "[]")]
    [InlineData(false, "not json")]
    [InlineData(false, "{\"summaryDTO\":{\"distance\":\"not-a-number\"}}")]
    public async Task Invalid_response_is_sanitized(bool authentication, string body)
    {
        await using var service = new ControlledService(path => (200,
            (path == ControlledService.TokenPath) == authentication ? body
                : path == ControlledService.TokenPath ? ControlledService.Token : ControlledService.ActivityJson));
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("invalid response", result.Error);
        Assert.DoesNotContain("Exception", result.Error);
        AssertSanitized(result);
    }

    [Fact]
    public async Task Help_explains_setup_without_credentials_or_http()
    {
        await using var service = new ControlledService();
        var result = await RunAsync(service, null, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Personal API Key", result.Output);
        Assert.Contains("GARMIN_PERSONAL_API_KEY", result.Output);
        Assert.Contains("connect.garmin.com/modern/activity/", result.Output);
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Secret_option_is_rejected_without_echoing_it()
    {
        await using var service = new ControlledService();
        var result = await RunAsync(service, null, "--api-key", Key, "42");
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(service.Requests);
        AssertSanitized(result);
    }

    [Fact]
    public async Task Local_time_is_labeled_without_inventing_an_offset()
    {
        await using var service = new ControlledService(path => (200, path == ControlledService.TokenPath
            ? ControlledService.Token : "{\"summaryDTO\":{\"startTimeLocal\":\"2026-01-02T10:30:00.000\"}}"));
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2026-01-02T10:30:00.000 (activity local time; UTC offset unknown)", result.Output);
    }

    [Fact]
    public async Task Connection_failure_is_sanitized()
    {
        var service = new ControlledService();
        await service.DisposeAsync();
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Could not connect", result.Error);
        AssertSanitized(result);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("https://example.com/?key=secret")]
    [InlineData("file:///tmp/secret")]
    public async Task Unsafe_endpoint_configuration_is_rejected(string endpoint)
    {
        var result = await RunAtAsync(new Uri(endpoint), Key, "42");
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("GARMIN_PERSONAL_API_BASE_URL", result.Error);
        Assert.DoesNotContain("secret", result.Error);
        AssertSanitized(result);
    }

    [Fact]
    public async Task Redirect_does_not_forward_the_key()
    {
        await using var destination = new ControlledService();
        await using var service = new ControlledService(_ => (302, destination.Address.AbsoluteUri));
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(destination.Requests);
        AssertSanitized(result);
    }

    [Fact]
    public async Task Names_cannot_inject_console_lines_or_echo_the_personal_key()
    {
        await using var service = new ControlledService(path => (200, path == ControlledService.TokenPath
            ? ControlledService.Token : "{\"activityName\":\"synthetic-test-key\\nInjected\\u001b[31m\"}"));
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Name: [redacted] Injected [31m", result.Output);
        Assert.DoesNotContain('\u001b', result.Output);
        AssertSanitized(result);
    }

    [Fact]
    public async Task Malformed_token_charset_cannot_leak_secrets_in_an_exception()
    {
        await using var service = new ControlledService(_ => (200, "invalid token"),
            tokenContentType: "text/plain; charset=" + Key);
        var result = await RunAsync(service, Key, "42");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("invalid response", result.Error);
        Assert.DoesNotContain("Exception", result.Error);
        AssertSanitized(result);
    }

    internal static Task<(int ExitCode, string Output, string Error)> RunAsync(
        ControlledService service, string? key, params string[] arguments)
        => RunAtAsync(service.Address, key, arguments);

    private static async Task<(int ExitCode, string Output, string Error)> RunAtAsync(
        Uri address, string? key, params string[] arguments)
    {
        var executable = Environment.GetEnvironmentVariable("GARMIN_TEST_EXECUTABLE");
        var info = new ProcessStartInfo(executable ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (executable is null)
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Garmin.Cli.dll"));
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment.Remove("GARMIN_PERSONAL_API_KEY");
        if (key is not null) info.Environment["GARMIN_PERSONAL_API_KEY"] = key;
        info.Environment["GARMIN_PERSONAL_API_BASE_URL"] = address.AbsoluteUri;
        // Catch culture-sensitive output and accidental local-time interpretation.
        info.Environment["LANG"] = "de_DE.UTF-8";
        info.Environment["TZ"] = "Pacific/Honolulu";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static void AssertSanitized((int ExitCode, string Output, string Error) result)
    {
        Assert.DoesNotContain(Key, result.Output + result.Error);
        Assert.DoesNotContain(ControlledService.Token, result.Output + result.Error);
    }
}
