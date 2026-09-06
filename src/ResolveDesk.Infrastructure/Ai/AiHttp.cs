using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Ai;

/// <summary>
/// Shared transport for every provider: builds the URL, applies the provider's auth scheme, posts
/// source-generated JSON and reads a source-generated response. Keeps the client classes to their
/// wire shapes only.
/// </summary>
internal static class AiHttp
{
    /// <summary>Base URL used when configuration leaves it empty — each provider's documented default.</summary>
    public static string DefaultBaseUrl(AiProvider provider) => provider switch
    {
        AiProvider.Ollama => "http://localhost:11434",
        AiProvider.OpenAi => "https://api.openai.com",
        AiProvider.Gemini => "https://generativelanguage.googleapis.com",
        _ => "",
    };

    public static string Root(AiEndpointOptions o) =>
        (string.IsNullOrWhiteSpace(o.BaseUrl) ? DefaultBaseUrl(o.Provider) : o.BaseUrl).TrimEnd('/');

    /// <summary>Full endpoint for a capability, accounting for Azure's deployment-scoped URL shape.</summary>
    public static string Url(AiEndpointOptions o, string openAiPath, string azurePath, string geminiMethod)
    {
        var root = Root(o);
        return o.Provider switch
        {
            AiProvider.AzureOpenAi =>
                $"{root}/openai/deployments/{Uri.EscapeDataString(o.Model)}/{azurePath}?api-version={o.ApiVersion ?? "2024-10-21"}",
            AiProvider.Gemini =>
                $"{root}/v1beta/models/{Uri.EscapeDataString(o.Model)}:{geminiMethod}",
            _ => $"{root}{openAiPath}",
        };
    }

    /// <summary>POST a request object and deserialize the response — the only network path in this layer.</summary>
    public static async Task<TRes?> PostAsync<TReq, TRes>(
        HttpClient http,
        AiEndpointOptions options,
        string url,
        TReq request,
        JsonTypeInfo<TReq> requestInfo,
        JsonTypeInfo<TRes> responseInfo,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, requestInfo), Encoding.UTF8, "application/json"),
        };
        Authenticate(message, options);

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new AiProviderException(
                $"{options.Provider} returned {(int)response.StatusCode} for {url}: {Truncate(body, 400)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, responseInfo, ct);
    }

    /// <summary>
    /// Reachability for a capability. For Ollama this asks whether the configured *model* is present,
    /// not just whether the daemon answers — a running Ollama without the model pulled would otherwise
    /// report healthy and then fail on the first real call.
    /// </summary>
    public static Task<bool> ProbeCapabilityAsync(HttpClient http, AiEndpointOptions o, CancellationToken ct) =>
        o.Provider switch
        {
            AiProvider.Ollama => ProbeOllamaModelAsync(http, o, ct),
            AiProvider.Gemini => ProbeAsync(http, o, $"{Root(o)}/v1beta/models", ct),
            AiProvider.AzureOpenAi => ProbeAsync(http, o, $"{Root(o)}/openai/models?api-version={o.ApiVersion ?? "2024-10-21"}", ct),
            _ => ProbeAsync(http, o, $"{Root(o)}/v1/models", ct),
        };

    private static async Task<bool> ProbeOllamaModelAsync(HttpClient http, AiEndpointOptions o, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{Root(o)}/api/tags");
            Authenticate(message, o);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var tags = await JsonSerializer.DeserializeAsync(stream, AiJsonContext.Default.OllamaTagsResponse, ct);
            if (tags?.Models is not { Count: > 0 } models) return false;

            return models.Any(m => m.Name is { } name && Matches(name, o.Model));
        }
        catch
        {
            return false;
        }

        // Ollama reports "name:tag". An untagged configuration means ":latest" — but a tagged one must
        // match exactly, so "qwen2.5-coder:14b" is not satisfied by an installed "qwen2.5-coder:7b".
        static bool Matches(string installed, string configured) =>
            installed.Equals(configured, StringComparison.OrdinalIgnoreCase) ||
            (!configured.Contains(':') &&
             installed.Equals($"{configured}:latest", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>GET used only by reachability probes.</summary>
    public static async Task<bool> ProbeAsync(HttpClient http, AiEndpointOptions options, string url, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            Authenticate(message, options);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void Authenticate(HttpRequestMessage message, AiEndpointOptions o)
    {
        var key = o.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) return;

        switch (o.Provider)
        {
            case AiProvider.AzureOpenAi:
                message.Headers.TryAddWithoutValidation("api-key", key);
                break;
            case AiProvider.Gemini:
                message.Headers.TryAddWithoutValidation("x-goog-api-key", key);
                break;
            default:
                // OpenAI and every compatible gateway; also lets a proxied Ollama sit behind a bearer token.
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                break;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

/// <summary>Raised when a provider answers with a non-success status; carries no credentials.</summary>
public sealed class AiProviderException(string message) : Exception(message);
