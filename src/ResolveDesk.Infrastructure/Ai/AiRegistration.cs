using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Ai;

public static class AiRegistration
{
    /// <summary>
    /// Registers the chat and embedding capabilities independently: either can be a local model, a
    /// hosted one, or switched off entirely, without the rest of the application knowing which.
    /// </summary>
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration config)
    {
        var options = AiOptions.Read(config);
        services.AddSingleton(options);

        // Typed clients stay transient so IHttpClientFactory keeps rotating handlers; only the
        // disabled stand-ins (which hold nothing) are singletons.
        if (options.ChatEnabled)
        {
            services.AddHttpClient<AiChatClient>(c => c.Timeout = TimeSpan.FromSeconds(options.Chat.TimeoutSeconds));
            services.AddTransient<IAiChatClient>(sp => sp.GetRequiredService<AiChatClient>());
        }
        else
        {
            services.AddSingleton<IAiChatClient, DisabledChatClient>();
        }

        if (options.EmbeddingEnabled)
        {
            services.AddHttpClient<AiEmbeddingClient>(c => c.Timeout = TimeSpan.FromSeconds(options.Embedding.TimeoutSeconds));
            services.AddTransient<IAiEmbeddingClient>(sp => sp.GetRequiredService<AiEmbeddingClient>());
        }
        else
        {
            services.AddSingleton<IAiEmbeddingClient, DisabledEmbeddingClient>();
        }

        return services;
    }
}
