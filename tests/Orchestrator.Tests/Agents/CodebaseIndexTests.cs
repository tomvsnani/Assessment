using Orchestrator.Agents.Codebase;

namespace Orchestrator.Tests.Agents;

public class CodebaseIndexTests
{
    private static readonly (string Path, string Source)[] Sources =
    [
        ("src/Core/Ports/ILinkRepository.cs", """
            namespace Core.Ports;
            public interface ILinkRepository
            {
                Task RecordClickAsync(ShortCode code, DateTimeOffset at, CancellationToken ct);
                Task<ClickStats?> GetStatsAsync(ShortCode code, CancellationToken ct);
            }
            """),
        ("src/Core/Links/LinkResolver.cs", """
            namespace Core.Links;
            public sealed class LinkResolver(ILinkRepository repository, ILinkCache cache)
            {
                public async Task<Link?> ResolveAsync(ShortCode code, CancellationToken ct)
                {
                    await repository.RecordClickAsync(code, DateTimeOffset.UtcNow, ct);
                    return null;
                }
            }
            """),
        ("src/Api/Endpoints/StatsEndpoint.cs", """
            namespace Api.Endpoints;
            public static class StatsEndpoint
            {
                public static void MapStats(IEndpointRouteBuilder app) { }
                private static Task<IResult> HandleAsync(string code, ILinkRepository repository) => throw null!;
            }
            """),
        ("src/Api/Program.cs", """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddScoped<LinkResolver>();
            """),
        ("src/Core/Links/ShortCode.cs", """
            namespace Core.Links;
            public readonly record struct ShortCode { public const int MinLength = 4; public string Value { get; } }
            """),
    ];

    [Fact]
    public void Given_sources_When_repo_map_built_Then_types_and_public_members_are_listed()
    {
        var map = RepoMap.Build(Sources);

        var resolver = map.Files.Single(f => f.Path.EndsWith("LinkResolver.cs", StringComparison.Ordinal));
        resolver.Types.Single().Name.Should().Be("LinkResolver(ILinkRepository repository, ILinkCache cache)");
        resolver.Types.Single().Members.Should().Equal("Task<Link?> ResolveAsync(ShortCode code, CancellationToken ct)");
        resolver.ReferencedIdentifiers.Should().Contain("ILinkRepository").And.Contain("RecordClickAsync");

        var rendered = map.Render();
        rendered.Should().Contain("interface ILinkRepository").And.Contain("Task RecordClickAsync(");
        rendered.Should().NotContain("HandleAsync", "private members are not part of the map");
    }

    [Fact]
    public void Given_requirement_about_click_counting_When_impact_analysed_Then_repository_ranks_first_and_referencing_files_follow()
    {
        var sources = Sources.ToDictionary(s => s.Path, s => s.Source, StringComparer.Ordinal);
        var terms = SeedTerms.FromRequirement("Every redirect does a synchronous `RecordClickAsync` before responding; move click counting off the redirect path. Stats stay.");

        var report = ImpactAnalysis.Analyze(RepoMap.Build(Sources), terms, sources);

        terms.Should().Contain("RecordClickAsync");
        report.Entries[0].Path.Should().Be("src/Core/Ports/ILinkRepository.cs");
        report.TopPaths(3).Should().Contain("src/Core/Links/LinkResolver.cs");
        report.Entries.Should().Contain(e => e.Path.EndsWith("StatsEndpoint.cs", StringComparison.Ordinal));
        report.Entries.Should().Contain(e => e.Path.EndsWith("Program.cs", StringComparison.Ordinal) && e.Reasons.Single().StartsWith("references LinkResolver", StringComparison.Ordinal));
        report.Entries.Should().NotContain(e => e.Path.EndsWith("ShortCode.cs", StringComparison.Ordinal) && e.Score > 5, "ShortCode is not about click counting");
        report.RenderMarkdown().Should().Contain("| File | Score | Why |");
    }

    [Fact]
    public void Given_same_input_When_analysed_twice_Then_output_is_identical()
    {
        var sources = Sources.ToDictionary(s => s.Path, s => s.Source, StringComparer.Ordinal);
        var terms = SeedTerms.FromRequirement("click counting stats redirect");

        var a = ImpactAnalysis.Analyze(RepoMap.Build(Sources), terms, sources).RenderMarkdown();
        var b = ImpactAnalysis.Analyze(RepoMap.Build(Sources), terms, sources).RenderMarkdown();

        a.Should().Be(b);
    }
}
