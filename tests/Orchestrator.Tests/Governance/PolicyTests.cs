using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;
using Orchestrator.Core.Governance.Policies;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Tests.Governance;

public class PolicyTests
{
    private static StageDefinition Stage(string id, string agent) =>
        new(id, agent, [], GateDefinition.Open, GateDefinition.Open, RetryDefinition.None, null, FailureHandling.StopRun);

    private static PolicyContext Context(
        StageDefinition? stage = null,
        IReadOnlyList<Artifact>? outputs = null,
        IReadOnlyDictionary<string, Artifact>? all = null,
        IReadOnlyList<Decision>? decisions = null,
        IReadOnlyList<ChangedFile>? files = null,
        IReadOnlyDictionary<string, string>? roles = null) =>
        new(stage ?? Stage("implement", "implementer"), outputs ?? [], all ?? new Dictionary<string, Artifact>(),
            decisions ?? [], files ?? [], roles ?? new Dictionary<string, string>());

    public class NoSecrets
    {
        private readonly NoSecretsPolicy _policy = new();

        [Theory]
        [InlineData("var key = \"sk-ant-api03-0123456789abcdefghijklmnopqrstuvwxyz\";", "Anthropic")]
        [InlineData("OPENAI=sk-proj-abcdefghijklmnopqrstuvwxyz0123456789ABCDEF", "OpenAI")]
        [InlineData("aws_access_key_id = AKIAIOSFODNN7EXAMPLE", "AWS")]
        [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIE...", "private key")]
        [InlineData("var cs = \"Host=db;Password=\\\"hunter2hunter2\\\"\"; // password = \"hunter2hunter2\"", "password")]
        public void Given_credential_like_content_in_changed_file_When_evaluated_Then_blocks(string content, string label)
        {
            var verdict = _policy.Evaluate(Context(files: [new ChangedFile("src/Db.cs", content)]));

            verdict.Outcome.Should().Be(PolicyOutcome.Block);
            verdict.Reason.Should().ContainEquivalentOf(label).And.Contain("src/Db.cs");
        }

        [Theory]
        [InlineData("var key = Environment.GetEnvironmentVariable(\"ANTHROPIC_API_KEY\");")]
        [InlineData("Password=${POSTGRES_PASSWORD}")]
        [InlineData("password: \"\"")]
        [InlineData("// see docs: keys look like sk-ant-...")]
        public void Given_placeholder_or_env_lookup_When_evaluated_Then_passes(string content)
        {
            _policy.Evaluate(Context(files: [new ChangedFile("a.cs", content)])).Outcome.Should().Be(PolicyOutcome.Pass);
        }

        [Fact]
        public void Given_secret_in_artifact_When_evaluated_Then_blocks_naming_the_artifact()
        {
            var artifact = new Artifact("design", ArtifactKind.Design, "use AKIAIOSFODNN7EXAMPLE", "design", []);

            _policy.Evaluate(Context(outputs: [artifact])).Reason.Should().Contain("artifact 'design'");
        }
    }

    public class PiiInLogs
    {
        private readonly PiiInLogsPolicy _policy = new();

        [Theory]
        [InlineData("logger.LogInformation(\"Redirecting {Code} to {TargetUrl}\", code, url);", "TargetUrl")]
        [InlineData("_log.LogWarning(\n  \"blocked {ClientIp}\",\n  ip);", "ClientIp")]
        [InlineData("logger.LogDebug(\"UA {UserAgent}\", ua);", "UserAgent")]
        public void Given_log_call_with_pii_placeholder_When_evaluated_Then_blocks(string code, string prop)
        {
            var verdict = _policy.Evaluate(Context(files: [new ChangedFile("Endpoints/Redirect.cs", code)]));

            verdict.Outcome.Should().Be(PolicyOutcome.Block);
            verdict.Reason.Should().Contain(prop);
        }

        [Theory]
        [InlineData("logger.LogInformation(\"Link {Code} -> {TargetHost}\", code, host);")]
        [InlineData("var x = $\"{TargetUrl}\"; // not a log call")]
        public void Given_safe_logging_When_evaluated_Then_passes(string code)
        {
            _policy.Evaluate(Context(files: [new ChangedFile("a.cs", code)])).Outcome.Should().Be(PolicyOutcome.Pass);
        }

        [Fact]
        public void Given_pii_placeholder_in_non_csharp_file_When_evaluated_Then_ignored()
        {
            _policy.Evaluate(Context(files: [new ChangedFile("notes.md", "LogInformation({TargetUrl})")])).Outcome.Should().Be(PolicyOutcome.Pass);
        }
    }

    public class SchemaChange
    {
        private readonly SchemaChangeNeedsApprovalPolicy _policy = new();
        private static readonly ChangedFile Migration = new("Postgres/Schema.cs", "CREATE TABLE IF NOT EXISTS click_outbox (id BIGSERIAL PRIMARY KEY);");
        private static readonly Decision DesignApproved = new("D001", "design", "ramu", DecisionKind.Approved, "ok", ["design"], DateTimeOffset.UtcNow);

        private static Dictionary<string, Artifact> DesignMentioning(string text) =>
            new() { ["design"] = new Artifact("design", ArtifactKind.Design, text, "design", ["spec"]) };

        [Fact]
        public void Given_no_ddl_When_evaluated_Then_passes()
        {
            _policy.Evaluate(Context(files: [new ChangedFile("a.cs", "SELECT 1")])).Outcome.Should().Be(PolicyOutcome.Pass);
        }

        [Fact]
        public void Given_ddl_and_approved_design_naming_the_table_When_evaluated_Then_passes()
        {
            var ctx = Context(all: DesignMentioning("Add a click_outbox table written in the same transaction."), decisions: [DesignApproved], files: [Migration]);

            _policy.Evaluate(ctx).Outcome.Should().Be(PolicyOutcome.Pass);
        }

        [Fact]
        public void Given_ddl_for_table_absent_from_design_When_evaluated_Then_blocks()
        {
            var ctx = Context(all: DesignMentioning("We will add an outbox table."), decisions: [DesignApproved], files: [Migration]);

            _policy.Evaluate(ctx).Reason.Should().Contain("click_outbox").And.Contain("not mentioned");
        }

        [Fact]
        public void Given_ddl_but_design_not_approved_When_evaluated_Then_blocks()
        {
            var ctx = Context(all: DesignMentioning("click_outbox"), decisions: [], files: [Migration]);

            _policy.Evaluate(ctx).Reason.Should().Contain("never approved");
        }

        [Fact]
        public void Given_ddl_but_no_design_When_evaluated_Then_blocks()
        {
            _policy.Evaluate(Context(files: [Migration])).Reason.Should().Contain("no design");
        }
    }

    public class SegregationOfDuties
    {
        private readonly SegregationOfDutiesPolicy _policy = new();
        private static readonly Dictionary<string, Artifact> Implementation =
            new() { ["implementation"] = new Artifact("implementation", ArtifactKind.Code, "diff", "implement", ["plan"]) };

        [Fact]
        public void Given_reviewer_role_differs_from_implementer_When_evaluated_Then_passes()
        {
            var ctx = Context(stage: Stage("review", "reviewer"), all: Implementation, roles: new Dictionary<string, string> { ["implement"] = "implementer" });

            _policy.Evaluate(ctx).Outcome.Should().Be(PolicyOutcome.Pass);
        }

        [Fact]
        public void Given_same_role_reviews_its_own_code_When_evaluated_Then_blocks()
        {
            var ctx = Context(stage: Stage("review", "implementer"), all: Implementation, roles: new Dictionary<string, string> { ["implement"] = "implementer" });

            _policy.Evaluate(ctx).Reason.Should().Contain("own implementation");
        }

        [Fact]
        public void Given_nothing_to_review_yet_When_evaluated_Then_passes()
        {
            _policy.Evaluate(Context(stage: Stage("review", "implementer"))).Outcome.Should().Be(PolicyOutcome.Pass);
        }
    }
}
