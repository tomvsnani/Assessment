namespace Shortener.Core.Links;

/// <summary>Closed set of outcomes; callers switch on the concrete type instead of catching exceptions.</summary>
public abstract record CreateLinkResult
{
    public sealed record Created(Link Link, bool WasExisting) : CreateLinkResult;
    public sealed record RejectedUrl(string Reason) : CreateLinkResult;
    public sealed record InvalidAlias(string Reason) : CreateLinkResult;
    public sealed record AliasTaken(string Alias) : CreateLinkResult;
    public sealed record CodeCollision : CreateLinkResult;
}
