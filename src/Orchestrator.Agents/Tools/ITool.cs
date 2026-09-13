using System.Text.Json;
using Orchestrator.Agents.Llm;

namespace Orchestrator.Agents.Tools;

/// <summary>A capability an LLM agent may call. Every tool is scoped to the run's workspace.</summary>
public interface ITool
{
    string Name { get; }

    string Description { get; }

    /// <summary>JSON schema for the input object.</summary>
    string InputSchema { get; }

    Task<string> InvokeAsync(JsonElement input, CancellationToken ct);
}

public static class ToolExtensions
{
    public static ToolDefinition ToDefinition(this ITool tool) =>
        new(tool.Name, tool.Description, JsonDocument.Parse(tool.InputSchema).RootElement.Clone());

    public static string Arg(this JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ArgumentException($"Tool input is missing string argument '{name}'.");

    public static string? OptionalArg(this JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
