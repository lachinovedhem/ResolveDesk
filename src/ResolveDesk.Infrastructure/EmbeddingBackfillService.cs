using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// Keeps the vector index in step with resolved tickets. Runs as a background loop rather than on the
/// request path so closing a ticket never waits on an embedding model, and so an index built after the
/// fact (or a switched model) catches up on its own.
///
/// Work is found by content hash, which makes the loop idempotent: an edited resolution is re-embedded,
/// an unchanged one is skipped, and a crash mid-batch simply resumes.
/// </summary>
public sealed class EmbeddingBackfillService(
    IServiceScopeFactory scopes,
    AiOptions ai,
    ILogger<EmbeddingBackfillService> logger) : BackgroundService
{
    private const int BatchSize = 32;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ai.EmbeddingEnabled)
        {
            logger.LogInformation("Embedding backfill disabled (Ai:Embedding:Provider = None).");
            return;
        }

        // Let the web host finish starting before touching the database.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;
            try
            {
                var processed = await RunBatchAsync(stoppingToken);
                // More work waiting — come straight back instead of sleeping.
                if (processed == BatchSize) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embedding backfill batch failed; retrying in {Delay}.", ErrorDelay);
                delay = ErrorDelay;
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var index = scope.ServiceProvider.GetRequiredService<ISemanticIndex>();
        var embeddings = scope.ServiceProvider.GetRequiredService<IAiEmbeddingClient>();

        if (!await index.IsAvailableAsync(ct)) return 0;

        var pending = await index.ListStaleAsync(BatchSize, ct);
        if (pending.Count == 0) return 0;

        var vectors = await embeddings.EmbedBatchAsync([.. pending.Select(p => p.Content)], ct);
        if (vectors.Count != pending.Count)
        {
            logger.LogWarning("Embedding provider returned {Got} vectors for {Want} inputs; skipping batch.",
                vectors.Count, pending.Count);
            return 0;
        }

        for (var i = 0; i < pending.Count; i++)
            await index.UpsertAsync(pending[i].TicketId, pending[i].ContentHash, vectors[i], embeddings.Model, ct);

        logger.LogInformation("Embedded {Count} resolved ticket(s) with {Model}.", pending.Count, embeddings.Model);
        return pending.Count;
    }
}
