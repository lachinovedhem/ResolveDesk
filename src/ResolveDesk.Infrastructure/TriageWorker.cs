using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

public sealed record TriageJob(long TicketId, string? Resolution);

public sealed class TriageQueue(ILogger<TriageQueue> logger) : ITriageQueue
{
    // Bounded: if the model cannot keep up, dropping the newest triage job is the right failure —
    // the ticket itself is safe, and the assessment can be re-run from the UI at any time.
    internal Channel<TriageJob> Channel { get; } =
        System.Threading.Channels.Channel.CreateBounded<TriageJob>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    public void EnqueueAssessment(long ticketId) => Write(new TriageJob(ticketId, null));

    public void EnqueueResolutionReview(long ticketId, string resolution) =>
        Write(new TriageJob(ticketId, resolution));

    private void Write(TriageJob job)
    {
        if (!Channel.Writer.TryWrite(job))
            logger.LogWarning("Triage queue is full; dropped job for ticket {TicketId}.", job.TicketId);
    }
}

public sealed class TriageWorker(
    TriageQueue queue,
    IServiceScopeFactory scopes,
    ILogger<TriageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (job.Resolution is { Length: > 0 } resolution)
                {
                    var reviews = scope.ServiceProvider.GetRequiredService<IResolutionReviewService>();
                    await reviews.ReviewAsync(job.TicketId, resolution, stoppingToken);
                }
                else
                {
                    // A ticket resolved while its job waited in the queue no longer needs triage:
                    // difficulty, effort and duplicate detection all describe work still to be done.
                    // Skipping costs a cheap read and saves a model call that nothing would read.
                    var repository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
                    var ticket = await repository.GetAsync(job.TicketId, stoppingToken);
                    if (ticket is null ||
                        ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
                    {
                        continue;
                    }

                    var assessments = scope.ServiceProvider.GetRequiredService<IAssessmentService>();
                    await assessments.AssessAsync(job.TicketId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad job must not stop the queue.
                logger.LogWarning(ex, "Triage job for ticket {TicketId} failed.", job.TicketId);
            }
        }
    }
}
