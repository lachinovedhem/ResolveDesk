using System.Text.Json.Serialization;

namespace ResolveDesk.Infrastructure.Ai;

// Wire contracts for the supported providers. Kept as plain records with explicit property names so a
// single source-generated context covers every provider — no reflection, so this survives Native AOT.

// ── OpenAI-compatible (OpenAI, Azure OpenAI, LM Studio, vLLM, llama.cpp, OpenRouter) ──────────────
public sealed record OpenAiMessage([property: JsonPropertyName("role")] string Role,
                                   [property: JsonPropertyName("content")] string Content);

public sealed record OpenAiChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiMessage> Messages,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("stream")] bool Stream);

public sealed record OpenAiChatChoice([property: JsonPropertyName("message")] OpenAiMessage? Message);
public sealed record OpenAiChatResponse([property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatChoice>? Choices);

public sealed record OpenAiEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

public sealed record OpenAiEmbeddingItem([property: JsonPropertyName("embedding")] float[]? Embedding,
                                         [property: JsonPropertyName("index")] int Index);
public sealed record OpenAiEmbeddingResponse([property: JsonPropertyName("data")] IReadOnlyList<OpenAiEmbeddingItem>? Data);

// ── Ollama (native API — richer than its OpenAI-compat shim, and never needs a key) ───────────────
public sealed record OllamaOptions([property: JsonPropertyName("temperature")] double Temperature);

public sealed record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("options")] OllamaOptions Options);

public sealed record OllamaChatResponse([property: JsonPropertyName("message")] OpenAiMessage? Message);

public sealed record OllamaEmbedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

public sealed record OllamaEmbedResponse([property: JsonPropertyName("embeddings")] IReadOnlyList<float[]>? Embeddings);

public sealed record OllamaTag([property: JsonPropertyName("name")] string? Name);
public sealed record OllamaTagsResponse([property: JsonPropertyName("models")] IReadOnlyList<OllamaTag>? Models);

// ── Google Gemini ────────────────────────────────────────────────────────────────────────────────
public sealed record GeminiPart([property: JsonPropertyName("text")] string? Text);
public sealed record GeminiContent([property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts,
                                   [property: JsonPropertyName("role")] string? Role);

public sealed record GeminiGenerateRequest(
    [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
    [property: JsonPropertyName("systemInstruction")] GeminiContent? SystemInstruction);

public sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiContent? Content);
public sealed record GeminiGenerateResponse([property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);

public sealed record GeminiEmbedRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("content")] GeminiContent Content);

public sealed record GeminiBatchEmbedRequest([property: JsonPropertyName("requests")] IReadOnlyList<GeminiEmbedRequest> Requests);

public sealed record GeminiEmbeddingValues([property: JsonPropertyName("values")] float[]? Values);
public sealed record GeminiEmbedResponse([property: JsonPropertyName("embedding")] GeminiEmbeddingValues? Embedding);
public sealed record GeminiBatchEmbedResponse([property: JsonPropertyName("embeddings")] IReadOnlyList<GeminiEmbeddingValues>? Embeddings);

// ── Structured outputs we ask the model for ──────────────────────────────────────────────────────
// snake_case keys because that is what open-weight models emit most reliably when shown a JSON
// example. Every field is nullable: a model that omits one must degrade, not throw.

public sealed record AssessmentPayload(
    [property: JsonPropertyName("difficulty")] int? Difficulty,
    [property: JsonPropertyName("estimated_minutes")] int? EstimatedMinutes,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("duplicate_of")] string? DuplicateOf,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("confidence")] double? Confidence);

public sealed record ReviewPayload(
    [property: JsonPropertyName("polished_reply")] string? PolishedReply,
    [property: JsonPropertyName("internal_note")] string? InternalNote,
    [property: JsonPropertyName("verdict")] string? Verdict,
    [property: JsonPropertyName("verdict_detail")] string? VerdictDetail);

/// <summary>Source-generated serializer for every provider contract above. Registered per HTTP call.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSerializable(typeof(OpenAiChatResponse))]
[JsonSerializable(typeof(OpenAiEmbeddingRequest))]
[JsonSerializable(typeof(OpenAiEmbeddingResponse))]
[JsonSerializable(typeof(OllamaChatRequest))]
[JsonSerializable(typeof(OllamaChatResponse))]
[JsonSerializable(typeof(OllamaEmbedRequest))]
[JsonSerializable(typeof(OllamaEmbedResponse))]
[JsonSerializable(typeof(OllamaTagsResponse))]
[JsonSerializable(typeof(GeminiGenerateRequest))]
[JsonSerializable(typeof(GeminiGenerateResponse))]
[JsonSerializable(typeof(GeminiEmbedRequest))]
[JsonSerializable(typeof(GeminiBatchEmbedRequest))]
[JsonSerializable(typeof(GeminiEmbedResponse))]
[JsonSerializable(typeof(GeminiBatchEmbedResponse))]
[JsonSerializable(typeof(AssessmentPayload))]
[JsonSerializable(typeof(ReviewPayload))]
internal sealed partial class AiJsonContext : JsonSerializerContext;
