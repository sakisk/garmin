using System.Net;

namespace Garmin.PersonalApi;

/// <summary>A sanitized failure; response bodies, request URLs and credentials are excluded.</summary>
public sealed class GarminApiException : Exception
{
    public GarminFailure Failure { get; }
    public HttpStatusCode? StatusCode { get; }

    internal GarminApiException(GarminFailure failure, HttpStatusCode? status = null)
        : base(failure switch
        {
            GarminFailure.Authentication => "Authentication rejected. Check that the Personal API key is valid and unexpired.",
            GarminFailure.ActivityUnavailable => "Activity unavailable. Check its ID, ownership and access permissions.",
            GarminFailure.InvalidActivity => "Invalid activity ID. Check the ID in the Garmin Connect activity URL.",
            GarminFailure.RateLimited => "Garmin rate limit reached. Try again later.",
            GarminFailure.InvalidResponse => "Garmin returned an invalid response. Try again later.",
            GarminFailure.Timeout => "Garmin request timed out. Check connectivity and try again.",
            GarminFailure.Connection => "Could not connect to Garmin. Check network connectivity and TLS certificates.",
            _ => "Garmin service failed. Try again later."
        })
    {
        Failure = failure;
        StatusCode = status;
    }
}

public enum GarminFailure
{
    Authentication, ActivityUnavailable, InvalidActivity, RateLimited,
    Service, InvalidResponse, Timeout, Connection
}
