using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Ai;

/// <summary>
/// One chat client covering every supported provider — the provider is a configuration value, not a
/// compile-time choice, so an operator can move between a local model and a hosted one by editing
/// <c>Ai:Chat:Provider</c> and restarting.
/// </summary>
public sealed class AiChatClient(HttpClient http, AiOptions options) : IAiChatClient
{
    private readonly AiEndpointOptions _o = options.Chat;

    public bool IsEnabled { get; } = options.ChatEnabled;
    public string ProviderName => _o.Provider.ToString();
    public string Model => _o.Model;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (!IsEnabled) return "";

        return _o.Provider switch
        {
            AiProvider.Ollama => await OllamaAsync(systemPrompt, userPrompt, ct),
            AiProvider.Gemini => await GeminiAsync(systemPrompt, userPrompt, ct),
            _ => await OpenAiAsync(systemPrompt, userPrompt, ct),
        };
    }

    /// <summary>Reachability probe for <c>/api/v1/ai/status</c> — never throws.</summary>
    public Task<bool> ProbeAsync(CancellationToken ct = default) =>
        IsEnabled ? AiHttp.ProbeCapabilityAsync(http, _o, ct) : Task.FromResult(false);

    private async Task<string> OpenAiAsync(string system, string user, CancellationToken ct)
    {
        var request = new OpenAiChatRequest(_o.Model,
            [new OpenAiMessage("system", system), new OpenAiMessage("user", user)],
            Temperature: 0.2, Stream: false);

        var response = await AiHttp.PostAsync(http, _o,
            AiHttp.Url(_o, "/v1/chat/completions", "chat/completions", "generateContent"),
            request, AiJsonContext.Default.OpenAiChatRequest, AiJsonContext.Default.OpenAiChatResponse, ct);

        return response?.Choices is { Count: > 0 } c ? c[0].Message?.Content ?? "" : "";
    }

    private async Task<string> OllamaAsync(string system, string user, CancellationToken ct)
    {
        var request = new OllamaChatRequest(_o.Model,
            [new OpenAiMessage("system", system), new OpenAiMessage("user", user)],
            Stream: false, Options: new OllamaOptions(0.2));

        var response = await AiHttp.PostAsync(http, _o, $"{AiHttp.Root(_o)}/api/chat",
            request, AiJsonContext.Default.OllamaChatRequest, AiJsonContext.Default.OllamaChatResponse, ct);

        return response?.Message?.Content ?? "";
    }

    private async Task<string> GeminiAsync(string system, string user, CancellationToken ct)
    {
        var request = new GeminiGenerateRequest(
            [new GeminiContent([new GeminiPart(user)], "user")],
            new GeminiContent([new GeminiPart(system)], null));

        var response = await AiHttp.PostAsync(http, _o,
            AiHttp.Url(_o, "", "", "generateContent"),
            request, AiJsonContext.Default.GeminiGenerateRequest, AiJsonContext.Default.GeminiGenerateResponse, ct);

        var parts = response?.Candidates is { Count: > 0 } c ? c[0].Content?.Parts : null;
        return parts is { Count: > 0 } ? string.Concat(parts.Select(p => p.Text)) : "";
    }
}

/// <summary>Stand-in used when <c>Ai:Enabled</c> is false or the chat provider is None.</summary>
public sealed class DisabledChatClient : IAiChatClient
{
    public bool IsEnabled => false;
    public string ProviderName => nameof(AiProvider.None);
    public string Model => "";
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
        Task.FromResult("");
}
