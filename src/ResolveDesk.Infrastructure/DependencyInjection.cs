using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResolveDesk.Application;
using ResolveDesk.Infrastructure.Ai;

namespace ResolveDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // The connection string comes from the environment or a secret store, never from source.
        //
        // The one exception is a developer's own machine, where a default that points at a local
        // PostgreSQL saves a step and reveals nothing. In any other environment its absence is fatal
        // and says so, rather than starting up and quietly connecting somewhere unintended — the same
        // rule the JWT signing key follows.
        var cs = config["DB_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(cs))
        {
            var environment = config["ASPNETCORE_ENVIRONMENT"] ?? "Production";
            if (!environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "DB_CONNECTION_STRING is not set. Supply it from the environment or a secret " +
                    "store; there is no default outside Development.");
            }
            cs = "Host=localhost;Port=5432;Database=resolvedesk;Username=postgres;Password=postgres";
        }

        services.AddSingleton(new DbConnectionFactory(cs));
        services.AddSingleton(SearchOptions.Load(config));

        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<TriageRepository>();

        services.AddAi(config);

        // Availability is cached inside the index, so it is a singleton on purpose.
        services.AddSingleton<ISemanticIndex, PgVectorIndex>();
        services.AddScoped<ISuggestionService, SuggestionService>();
        services.AddScoped<IAssessmentService, AssessmentService>();
        services.AddScoped<IResolutionReviewService, ResolutionReviewService>();
        services.AddScoped<IRoutingService, RoutingService>();
        services.AddScoped<INotifier, Notifier>();

        // The hub holds live SSE connections, so it must outlive any single request.
        services.AddSingleton<INotificationHub, NotificationHub>();
        services.AddSingleton<TriageQueue>();
        services.AddSingleton<ITriageQueue>(sp => sp.GetRequiredService<TriageQueue>());

        services.AddHostedService<EmbeddingBackfillService>();
        services.AddHostedService<TriageWorker>();
        services.AddHostedService<SlaWatchService>();

        return services;
    }
}
