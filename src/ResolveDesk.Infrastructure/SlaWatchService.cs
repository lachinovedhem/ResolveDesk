using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// Warns about tickets that are about to breach their SLA, while there is still time to act.
///
/// The warning goes to the assignee and, for unassigned tickets, to the coordinators — an unassigned
/// ticket running out of time is a routing failure, not an agent's problem. Each ticket is warned
/// about once; the notifications table itself is what records that.
/// </summary>
public sealed class SlaWatchService(
    IServiceScopeFactory scopes,
    ILogger<SlaWatchService> logger) : BackgroundService
{
    /// <summary>How far ahead to look. Long enough to be actionable, short enough to still be urgent.</summary>
    private const int WarnWithinMinutes = 60;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Usually the database being unreachable. Log once per sweep and carry on.
                logger.LogWarning(ex, "SLA sweep failed; retrying in {Interval}.", Interval);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var triage = scope.ServiceProvider.GetRequiredService<TriageRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var risks = await triage.SlaRisksAsync(WarnWithinMinutes, ct);
        if (risks.Count == 0) return;

        IReadOnlyList<User>? coordinators = null;

        foreach (var risk in risks)
        {
            var remaining = risk.SlaDueAtUtc - DateTime.UtcNow;
            var body = remaining > TimeSpan.Zero
                ? $"\"{risk.Title}\" is due in {(int)remaining.TotalMinutes} minutes."
                : $"\"{risk.Title}\" is past its SLA.";

            if (risk.AssigneeId is { } assignee)
            {
                await notifier.NotifyAsync(assignee, NotificationKind.SlaRisk, risk.TicketId,
                    $"{risk.Reference}: SLA at risk", body, ct);
            }
            else
            {
                coordinators ??= await users.ListAsync(UserRole.Coordinator, ct);
                foreach (var coordinator in coordinators)
                {
                    await notifier.NotifyAsync(coordinator.Id, NotificationKind.SlaRisk, risk.TicketId,
                        $"{risk.Reference}: unassigned and SLA at risk", body, ct);
                }
            }
        }

        logger.LogInformation("Warned about {Count} ticket(s) approaching their SLA.", risks.Count);
    }
}
