using Shortener.Core.Links;
using Shortener.Infrastructure.InMemory;
using Shortener.UnitTests.Fakes;

namespace Shortener.UnitTests.Links;

public class CreateLinkHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryLinkRepository _repository = new();
    private readonly FakeClock _clock = new(Now);

    private CreateLinkHandler Handler(params string[] codes) =>
        new(_repository, new SequenceShortCodeGenerator(codes), new LinkPolicy(), _clock);

    [Fact]
    public async Task Given_valid_url_When_created_Then_link_is_stored_with_generated_code()
    {
        var result = await Handler("abc1234").HandleAsync(new CreateLinkCommand("https://example.com"), CancellationToken.None);

        var created = result.Should().BeOfType<CreateLinkResult.Created>().Subject;
        created.WasExisting.Should().BeFalse();
        created.Link.Code.Value.Should().Be("abc1234");
        created.Link.CreatedAt.Should().Be(Now);
        created.Link.ExpiresAt.Should().BeNull();
        (await _repository.FindAsync(created.Link.Code, CancellationToken.None)).Should().Be(created.Link);
    }

    [Fact]
    public async Task Given_ttl_When_created_Then_expiry_is_relative_to_now()
    {
        var result = await Handler("abc1234").HandleAsync(
            new CreateLinkCommand("https://example.com", TimeToLive: TimeSpan.FromHours(1)), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.Created>()
            .Which.Link.ExpiresAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public async Task Given_rejected_url_When_created_Then_returns_RejectedUrl_and_stores_nothing()
    {
        var generator = new SequenceShortCodeGenerator("abc1234");
        var handler = new CreateLinkHandler(_repository, generator, new LinkPolicy(), _clock);

        var result = await handler.HandleAsync(new CreateLinkCommand("http://localhost/x"), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.RejectedUrl>();
        generator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Given_code_collision_When_created_Then_retries_with_next_code()
    {
        await Handler("taken01").HandleAsync(new CreateLinkCommand("https://first.example"), CancellationToken.None);

        var result = await Handler("taken01", "free001").HandleAsync(new CreateLinkCommand("https://second.example"), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.Created>().Which.Link.Code.Value.Should().Be("free001");
    }

    [Fact]
    public async Task Given_every_generated_code_collides_When_created_Then_returns_CodeCollision()
    {
        await Handler("same001").HandleAsync(new CreateLinkCommand("https://first.example"), CancellationToken.None);

        var result = await Handler("same001", "same001", "same001", "same001")
            .HandleAsync(new CreateLinkCommand("https://second.example"), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.CodeCollision>();
    }

    [Fact]
    public async Task Given_custom_alias_When_created_Then_alias_is_used_verbatim()
    {
        var result = await Handler().HandleAsync(
            new CreateLinkCommand("https://example.com", CustomAlias: "launch2026"), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.Created>().Which.Link.Code.Value.Should().Be("launch2026");
    }

    [Fact]
    public async Task Given_alias_already_used_When_created_Then_returns_AliasTaken()
    {
        await Handler().HandleAsync(new CreateLinkCommand("https://a.example", CustomAlias: "promo"), CancellationToken.None);

        var result = await Handler().HandleAsync(new CreateLinkCommand("https://b.example", CustomAlias: "promo"), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.AliasTaken>().Which.Alias.Should().Be("promo");
    }

    [Fact]
    public async Task Given_malformed_alias_When_created_Then_returns_InvalidAlias()
    {
        var result = await Handler().HandleAsync(
            new CreateLinkCommand("https://example.com", CustomAlias: "bad alias!"), CancellationToken.None);

        result.Should().BeOfType<CreateLinkResult.InvalidAlias>();
    }

    [Fact]
    public async Task Given_same_idempotency_key_When_created_twice_Then_second_call_returns_original()
    {
        var first = await Handler("abc1234").HandleAsync(
            new CreateLinkCommand("https://example.com", IdempotencyKey: "req-1"), CancellationToken.None);
        var second = await Handler("zzz9999").HandleAsync(
            new CreateLinkCommand("https://example.com", IdempotencyKey: "req-1"), CancellationToken.None);

        var replay = second.Should().BeOfType<CreateLinkResult.Created>().Subject;
        replay.WasExisting.Should().BeTrue();
        replay.Link.Should().Be(((CreateLinkResult.Created)first).Link);
    }
}
