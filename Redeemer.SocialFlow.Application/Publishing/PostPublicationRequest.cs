using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Application.Publishing;

/// <summary>Immutable publication input. Content and opaque destination are preserved verbatim.</summary>
public sealed record PostPublicationRequest
{
    public string Content { get; }
    public SocialPlatform Platform { get; }
    /// <summary>Provider-specific target identity, not credentials. No format or lookup capability is assumed.</summary>
    public string Destination { get; }
    /// <summary>Stable identity assigned by the caller for one logical operation, including its reconciliation.</summary>
    public Guid OperationId { get; }

    public PostPublicationRequest(string content, SocialPlatform platform, string destination, Guid operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (!Enum.IsDefined(platform)) throw new ArgumentException("Invalid platform.", nameof(platform));
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
        Content = content;
        Platform = platform;
        Destination = destination;
        OperationId = operationId;
    }
}
