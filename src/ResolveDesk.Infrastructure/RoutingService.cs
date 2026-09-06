using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// Recommends who should take a ticket, from three measurable signals:
///
///   • **Track record** — of the similar tickets retrieved from the archive, how many did this
///     person resolve. The strongest signal: they have literally done this before.
///   • **Skills** — how many of their declared skill words appear in the ticket text.
///   • **Load** — how many unfinished tickets they already hold.
///
/// No model is involved, on purpose. A coordinator overrules a recommendation far more readily when
/// they can see the three numbers behind it, and the numbers are testable. The model's contribution
/// arrives upstream, as the difficulty and category on the assessment.
/// </summary>
public sealed class RoutingService(
    ITicketRepository tickets,
    IUserRepository users,
    TriageRepository triage,
    ISuggestionService suggestions) : IRoutingService
{
    // Track record dominates; load only breaks ties between comparable people.
    private const double TrackRecordWeight = 0.5;
    private const double SkillWeight = 0.3;
    private const double AvailabilityWeight = 0.2;

    public Task<RoutingRecommendation?> GetAsync(long ticketId, CancellationToken ct = default) =>
        triage.GetRoutingAsync(ticketId, ct);

    public async Task<RoutingRecommendation> RecommendAsync(long ticketId, CancellationToken ct = default)
    {
        var ticket = await tickets.GetAsync(ticketId, ct)
            ?? throw new KeyNotFoundException($"Ticket {ticketId} not found.");

        var agents = await users.ListAsync(UserRole.Agent, ct);
        if (agents.Count == 0)
        {
            var empty = new RoutingRecommendation(null, null, "No active agents to route to.", [], DateTime.UtcNow);
            await triage.SaveRoutingAsync(ticketId, empty, ct);
            return empty;
        }

        // draftAnswer: false — only the matched ticket ids are read below. Drafting an answer here
        // added a whole chat call to every routing request, for text nothing ever displayed.
        var similar = await suggestions.SuggestForTicketAsync(ticketId, 8, draftAnswer: false, ct);
        var solvers = await triage.AssigneesOfAsync([.. similar.Matches.Select(m => m.SourceTicketId)], ct);
        var loads = (await triage.LoadPerAgentAsync(ct)).ToDictionary(l => l.UserId, l => l.OpenTickets);

        var solvedByAgent = solvers
            .GroupBy(s => s.AssigneeId)
            .ToDictionary(g => g.Key, g => g.Count());

        var haystack = $"{ticket.Title} {ticket.Description} {ticket.Category}".ToLowerInvariant();
        var maxSolved = solvedByAgent.Count == 0 ? 0 : solvedByAgent.Values.Max();
        var maxLoad = loads.Count == 0 ? 0 : loads.Values.DefaultIfEmpty(0).Max();

        var candidates = agents.Select(agent =>
        {
            var solved = solvedByAgent.GetValueOrDefault(agent.Id);
            var load = loads.GetValueOrDefault(agent.Id);
            var skillMatches = CountSkillMatches(agent.Skills, haystack);

            // Each signal is normalised to 0..1 before weighting, so one of them cannot dominate
            // just because it happens to be counted on a larger scale.
            var trackRecord = maxSolved == 0 ? 0 : (double)solved / maxSolved;
            var skill = skillMatches == 0 ? 0 : Math.Min(1.0, skillMatches / 2.0);
            var availability = maxLoad == 0 ? 1 : 1 - (double)load / maxLoad;

            var score = TrackRecordWeight * trackRecord + SkillWeight * skill + AvailabilityWeight * availability;

            return new RoutingCandidate(
                agent.Id, agent.FullName, agent.Skills,
                OpenTickets: load, SolvedSimilar: solved, SkillMatches: skillMatches,
                Score: Math.Round(score, 3),
                Reason: Explain(solved, skillMatches, load));
        })
        .OrderByDescending(c => c.Score)
        .ThenBy(c => c.OpenTickets)
        .ToList();

        var best = candidates[0];

        // Every signal at zero means we are recommending on nothing but an empty queue. Say so
        // rather than dressing up "whoever is least busy" as a match.
        var reason = best is { SolvedSimilar: 0, SkillMatches: 0 }
            ? $"No track record or skill match for this subject; {best.FullName} simply has the lightest queue ({best.OpenTickets} open)."
            : best.Reason;

        var recommendation = new RoutingRecommendation(
            best.UserId, best.FullName, reason, candidates, DateTime.UtcNow);
        await triage.SaveRoutingAsync(ticketId, recommendation, ct);
        return recommendation;
    }

    /// <summary>
    /// Skills are a free-text, comma-separated list. Matching is word-boundary-free on purpose —
    /// "routing" should match "routes" and "router" in a customer's own wording.
    /// </summary>
    private static int CountSkillMatches(string? skills, string haystack)
    {
        if (string.IsNullOrWhiteSpace(skills)) return 0;
        return skills
            .Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(s => s.Length >= 3)
            .Count(haystack.Contains);
    }

    private static string Explain(int solved, int skillMatches, int load)
    {
        var parts = new List<string>(3);
        if (solved > 0) parts.Add($"resolved {solved} similar ticket{(solved == 1 ? "" : "s")}");
        if (skillMatches > 0) parts.Add($"{skillMatches} skill match{(skillMatches == 1 ? "" : "es")}");
        parts.Add($"{load} open ticket{(load == 1 ? "" : "s")}");
        return string.Join(", ", parts) + ".";
    }
}
