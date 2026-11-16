using Garmin.PersonalApi;

namespace Garmin.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task Returns_typed_data_and_reuses_unexpired_token_in_memory()
    {
        await using var service = new ControlledService();
        using var http = new HttpClient();
        var client = new GarminClient(http, "synthetic-key", service.Address);
        var activity = await client.GetActivityAsync(42);
        Assert.Equal(42L, activity.ActivityId);
        Assert.Equal("running", activity.ActivityType?.TypeKey);
        Assert.Equal(5000.25, activity.Summary?.Distance);
        Assert.Equal(1800.5, activity.Summary?.Duration);
        Assert.Equal(new DateTime(2026, 1, 2, 9, 30, 0), activity.Summary?.StartTimeGmt);
        await client.GetActivityAsync(43);
        Assert.Equal(3, service.Requests.Count);
        Assert.Equal("/activity-service/activity/43", service.Requests.Last().Path);
    }

    [Fact]
    public async Task Replaces_token_near_expiration_without_writing_files()
    {
        var token = ControlledService.MakeToken(DateTimeOffset.UtcNow.AddSeconds(20));
        await using var service = new ControlledService(path => (200,
            path == ControlledService.TokenPath ? token : ControlledService.ActivityJson));
        using var http = new HttpClient();
        var client = new GarminClient(http, "synthetic-key", service.Address);
        await client.GetActivityAsync(42);
        await client.GetActivityAsync(42);
        Assert.Equal(2, service.Requests.Count(r => r.Path == ControlledService.TokenPath));
    }

    [Fact]
    public async Task Missing_fields_are_null_in_library_model()
    {
        await using var service = new ControlledService(path => (200,
            path == ControlledService.TokenPath ? ControlledService.Token : "{\"summaryDTO\":{\"distance\":0}}"));
        using var http = new HttpClient();
        var activity = await new GarminClient(http, "synthetic-key", service.Address).GetActivityAsync(42);
        Assert.Null(activity.ActivityId);
        Assert.Null(activity.ActivityName);
        Assert.Null(activity.ActivityType);
        Assert.Null(activity.Summary?.Duration);
        Assert.Null(activity.Summary?.StartTimeGmt);
        Assert.Equal(0, activity.Summary?.Distance);
    }

    [Fact]
    public async Task Timeout_covers_a_slow_response_body()
    {
        await using var service = new ControlledService(bodyDelay: TimeSpan.FromSeconds(1));
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
        var ex = await Assert.ThrowsAsync<GarminApiException>(() =>
            new GarminClient(http, "synthetic-key", service.Address).GetActivityAsync(42));
        Assert.Equal(GarminFailure.Timeout, ex.Failure);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        await using var service = new ControlledService();
        using var http = new HttpClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GarminClient(http, "synthetic-key", service.Address).GetActivityAsync(42, cancellation.Token));
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Status_is_available_but_server_error_body_is_not()
    {
        await using var service = new ControlledService(_ => (401, "private-response synthetic-key"));
        using var http = new HttpClient();
        var ex = await Assert.ThrowsAsync<GarminApiException>(() =>
            new GarminClient(http, "synthetic-key", service.Address).GetActivityAsync(42));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(GarminFailure.Authentication, ex.Failure);
        Assert.DoesNotContain("private-response", ex.ToString());
        Assert.DoesNotContain("synthetic-key", ex.ToString());
        Assert.Null(ex.InnerException);
    }
}
