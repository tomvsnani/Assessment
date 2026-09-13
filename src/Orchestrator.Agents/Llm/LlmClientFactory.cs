namespace Orchestrator.Agents.Llm;

/// <summary>
/// Picks a provider from configuration. Keys are read from the environment only
/// (<c>ANTHROPIC_API_KEY</c>, <c>OPENAI_API_KEY</c>, <c>GEMINI_API_KEY</c>) and never written anywhere.
/// </summary>
public static class LlmClientFactory
{
    public static readonly string[] Providers = ["anthropic", "openai", "gemini"];

    public static ILlmClient Create(string provider, string? model, HttpClient http)
    {
        var envVar = provider.ToUpperInvariant() + "_API_KEY";
        var key = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"Live mode needs {envVar} in the environment (provider '{provider}').");
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
