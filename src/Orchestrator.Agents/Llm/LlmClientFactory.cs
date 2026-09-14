namespace Orchestrator.Agents.Llm;

/// <summary>
/// Picks a provider from configuration. Keys are read from the environment only
/// (<c>ANTHROPIC_API_KEY</c>, <c>OPENAI_API_KEY</c>, <c>GEMINI_API_KEY</c>) and never written anywhere.
/// </summary>
public static class LlmClientFactory
{
    public static readonly string[] Providers = ["anthropic", "openai", "gemini"];

    public static string KeyVariable(string provider) => provider.ToUpperInvariant() + "_API_KEY";

    public static bool HasKey(string provider) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable(provider)));

    /// <summary>LLM_PROVIDER if set, else the first provider with a key in the environment, else anthropic.</summary>
    public static string DefaultProvider() =>
        Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? Providers.FirstOrDefault(HasKey) ?? "anthropic";

    public static string DefaultModel(string provider) => provider switch
    {
        "anthropic" => AnthropicClient.DefaultModel,
        "openai" => OpenAiClient.DefaultModel,
        "gemini" => GeminiClient.DefaultModel,
        _ => throw new ArgumentException($"Unknown provider '{provider}'.", nameof(provider)),
    };

    public static ILlmClient Create(string provider, string? model, HttpClient http)
    {
        var key = Environment.GetEnvironmentVariable(KeyVariable(provider));
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"Live mode needs {KeyVariable(provider)} in the environment (provider '{provider}'). Keys present for: {string.Join(", ", Providers.Where(HasKey).DefaultIfEmpty("none"))}.");
        }

        http.Timeout = TimeSpan.FromMinutes(10);
        return provider switch
        {
            "anthropic" => new AnthropicClient(http, key, model ?? AnthropicClient.DefaultModel),
            "openai" => new OpenAiClient(http, key, model ?? OpenAiClient.DefaultModel),
            "gemini" => new GeminiClient(http, key, model ?? GeminiClient.DefaultModel),
            _ => throw new ArgumentException($"Unknown provider '{provider}'. Use one of: {string.Join(", ", Providers)}.", nameof(provider)),
        };
    }
}
