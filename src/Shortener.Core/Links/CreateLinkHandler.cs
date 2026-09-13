using Shortener.Core.Ports;

namespace Shortener.Core.Links;

/// <summary>Use case: create a short link. Orchestrates policy, code generation and persistence.</summary>
public sealed class CreateLinkHandler(
    ILinkRepository repository,
    IShortCodeGenerator codes,
    LinkPolicy policy,
    IClock clock)
{
    /// <summary>Random codes are 7 chars (62^7 is about 3.5e12); collisions are rare, so a few retries suffice.</summary>
    private const int GeneratedCodeLength = 7;
    private const int MaxCollisionRetries = 3;

    public async Task<CreateLinkResult> HandleAsync(CreateLinkCommand command, CancellationToken ct)
    {
        if (policy.Reject(command.TargetUrl) is { } reason)
        {
            return new CreateLinkResult.RejectedUrl(reason);
        }

        if (command.IdempotencyKey is { } key
            && await repository.FindByIdempotencyKeyAsync(key, ct) is { } existing)
        {
            return new CreateLinkResult.Created(existing, WasExisting: true);
        }

        var now = clock.UtcNow;
        var target = new Uri(command.TargetUrl, UriKind.Absolute);
        var expiresAt = command.TimeToLive is { } ttl ? now.Add(ttl) : (DateTimeOffset?)null;

        return command.CustomAlias is { } alias
            ? await CreateWithAliasAsync(alias, target, now, expiresAt, command.IdempotencyKey, ct)
            : await CreateWithGeneratedCodeAsync(target, now, expiresAt, command.IdempotencyKey, ct);
    }

    private async Task<CreateLinkResult> CreateWithAliasAsync(
        string alias, Uri target, DateTimeOffset now, DateTimeOffset? expiresAt, string? idempotencyKey, CancellationToken ct)
    {
        if (!ShortCode.TryParse(alias, out var code))
        {
            return new CreateLinkResult.InvalidAlias(
                $"Alias must be {ShortCode.MinLength}-{ShortCode.MaxLength} alphanumeric characters.");
        }

        var link = new Link(code, target, now, expiresAt, idempotencyKey);
        return await repository.TryAddAsync(link, ct)
            ? new CreateLinkResult.Created(link, WasExisting: false)
            : new CreateLinkResult.AliasTaken(alias);
    }

    private async Task<CreateLinkResult> CreateWithGeneratedCodeAsync(
        Uri target, DateTimeOffset now, DateTimeOffset? expiresAt, string? idempotencyKey, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxCollisionRetries; attempt++)
        {
            var link = new Link(codes.Generate(GeneratedCodeLength), target, now, expiresAt, idempotencyKey);
            if (await repository.TryAddAsync(link, ct))
            {
                return new CreateLinkResult.Created(link, WasExisting: false);
            }
        }

        return new CreateLinkResult.CodeCollision();
    }
}
