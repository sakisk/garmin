# Sakisk.Garmin.PersonalApi

A small .NET 11 client for retrieving one Garmin Connect activity through the official Personal API. Compatible with trimming and Native AOT; no runtime package dependencies.

```csharp
using Garmin.PersonalApi;

// Your application supplies the key from its chosen secret store.
using var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
var client = new GarminClient(http, personalApiKey);
Activity activity = await client.GetActivityAsync(123456789, cancellationToken);
double? metres = activity.Summary?.Distance;
double? seconds = activity.Summary?.Duration;
```

The caller owns `HttpClient`. Use one `GarminClient` per key and invoke it sequentially. It exchanges the key for a plain-text bearer JWT and keeps that token in memory, renewing it near expiration. No files, environment discovery, logging, console output or process exits occur in the library. Configure HTTP logging in consuming applications to exclude authorization headers.

`Activity`, `ActivityType` and `ActivitySummary` expose a typed subset of Garmin's `ActivitySummaryDTO`; unmodeled fields are ignored and missing values remain null. `StartTimeGmt` represents UTC even though Garmin sends it without an offset suffix and .NET parses its `Kind` as `Unspecified`. Do not call `ToUniversalTime()` on that offset-free value: use `DateTime.SpecifyKind(value, DateTimeKind.Utc)`. `StartTimeLocal` is the activity's local clock time with unknown UTC offset. Distance is in metres and duration in seconds.

`GarminApiException` exposes `Failure` and an optional HTTP `StatusCode`. Its message is sanitized and contains no response body, request URL or underlying exception. Caller cancellation propagates as `OperationCanceledException`; network and request-budget timeouts become typed API failures. Invalid caller configuration or IDs raise argument exceptions. Authentication failures invalidate the cached token for a subsequent call; requests are not retried automatically.

The optional `baseAddress` constructor argument accepts an HTTPS origin or HTTP loopback for controlled tests. The default is `https://personal.connectapi.garmin.com/`. Disable automatic redirects on the caller's handler to avoid forwarding credentials.

Contracts: [Garmin authentication FAQ](https://developer.garmin.com/gc-developer-program/personal/personal-faq/), [official Swagger](https://developer.garmin.com/gc-developer-program/personal/swagger-docs/), [Garmin's reference authenticator](https://github.com/garmin/connect-personal-api-python/blob/main/src/garmin_personal_api/auth.py).
