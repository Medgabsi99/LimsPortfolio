namespace Lims.Core;

/// <summary>
/// Thrown when a caller violates a LIMS business rule intentionally
/// (e.g. invalid state transition, unknown client code, duplicate sample).
/// Maps to HTTP 400 Bad Request in the REST API exception middleware.
///
/// Use this instead of <see cref=""InvalidOperationException""/> so that
/// unrelated .NET internal errors are not accidentally surfaced as 400 responses.
/// </summary>
public class LimsBusinessException : Exception
{
    public LimsBusinessException(string message) : base(message) { }

    public LimsBusinessException(string message, Exception innerException)
        : base(message, innerException) { }
}
