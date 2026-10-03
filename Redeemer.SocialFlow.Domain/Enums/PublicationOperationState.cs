namespace Redeemer.SocialFlow.Domain.Enums;

public enum PublicationOperationState
{
    // Also used before dispatch: interruption must never make the operation resendable.
    Indeterminate = 1,
    Confirmed = 2,
    Failed = 3
}
