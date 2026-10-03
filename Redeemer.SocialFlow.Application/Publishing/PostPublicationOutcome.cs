namespace Redeemer.SocialFlow.Application.Publishing;

public enum PostPublicationOutcome
{
    /// <summary>The provider attests that publication occurred.</summary>
    Confirmed = 1,
    /// <summary>Publication certainly did not occur; this is not a statement about retryability.</summary>
    Failed = 2,
    /// <summary>Publication may have occurred, but is not confirmed. Reconcile before considering a retry.</summary>
    Indeterminate = 3
}
