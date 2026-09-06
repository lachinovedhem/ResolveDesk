using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using ResolveDesk.Application;
using ResolveDesk.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol itself, so every log line has to leave by stderr — a single stray
// Console.WriteLine corrupts the stream and the client disconnects.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var apiUrl = builder.Configuration["RESOLVEDESK_API_URL"] ?? "http://localhost:8080";
var apiToken = builder.Configuration["RESOLVEDESK_API_TOKEN"];

builder.Services.AddHttpClient<ResolveDeskClient>(c =>
{
    c.BaseAddress = new Uri(apiUrl);
    // Generous: a local model drafting an answer can take a while on CPU.
    c.Timeout = TimeSpan.FromSeconds(240);
    if (!string.IsNullOrWhiteSpace(apiToken))
        c.DefaultRequestHeaders.Authorization = new("Bearer", apiToken);
});

// Scoped, not singleton: it holds the typed HttpClient, whose handler the factory rotates.
builder.Services.AddScoped(sp => BuildTriageAgent(sp, builder.Configuration));

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "resolvedesk", Version = "0.1.0" };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

/// <summary>
/// Builds the Semantic Kernel used for triage, or returns an unavailable agent when no
/// OpenAI-compatible chat endpoint is configured. One connector serves Ollama, LM Studio, vLLM and
/// OpenAI itself — they differ only in endpoint and model.
/// </summary>
static TriageAgent BuildTriageAgent(IServiceProvider sp, IConfiguration config)
{
    var ai = AiOptions.Read(config);
    var chat = ai.Chat;

    if (!ai.ChatEnabled || chat.Provider is AiProvider.Gemini || string.IsNullOrWhiteSpace(chat.Model))
        return new TriageAgent(null, "");

    var root = (string.IsNullOrWhiteSpace(chat.BaseUrl)
        ? chat.Provider == AiProvider.Ollama ? "http://localhost:11434" : "https://api.openai.com"
        : chat.BaseUrl).TrimEnd('/');

    // Every OpenAI-compatible server exposes the same /v1 prefix, Ollama's compatibility shim included.
    var endpoint = root.EndsWith("/v1", StringComparison.Ordinal) ? root : $"{root}/v1";

    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.Services.AddLogging(l => l.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: chat.Model,
        endpoint: new Uri(endpoint),
        // Local servers ignore this; it is a placeholder, not a credential. Real keys arrive through
        // Ai__Chat__ApiKey in the environment.
        apiKey: string.IsNullOrWhiteSpace(chat.ApiKey) ? "local" : chat.ApiKey);

    var kernel = kernelBuilder.Build();
    kernel.Plugins.AddFromObject(new ResolveDeskPlugin(sp.GetRequiredService<ResolveDeskClient>()), "resolvedesk");
    return new TriageAgent(kernel, chat.Model);
}
