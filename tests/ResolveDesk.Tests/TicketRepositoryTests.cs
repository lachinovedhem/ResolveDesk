using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Tests;

/// <summary>
/// Repository behaviour against a real PostgreSQL. Every test here corresponds to something that a
/// clean compile did not catch — the build was 0 warnings, 0 errors while all of it was broken.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TicketRepositoryTests(DatabaseFixture db)
{
    private ITicketRepository Tickets => db.Resolve<ITicketRepository>();
    private IUserRepository Users => db.Resolve<IUserRepository>();

    private static TicketCreate NewTicket(string title, string description, string customer = "Test Customer") =>
        new(title, description, TicketPriority.Normal, TicketSource.Phone, "Connectivity", customer, null);

    private async Task<long> SeedResolvedAsync(string title, string description, string resolution)
    {
        var id = await Tickets.CreateAsync(NewTicket(title, description), null);
        await Tickets.SetStatusAsync(id, TicketStatus.Resolved, resolution);
        return id;
    }

    // ── Bug 1: Npgsql returns timestamptz as DateTime; DateTimeOffset threw on every read ──────────

    [RequiresDatabaseFact]
    public async Task Timestamps_round_trip_and_come_back_as_utc()
    {
        await db.ResetAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        var id = await Tickets.CreateAsync(NewTicket("Line drops", "It drops each evening."), null);
        var ticket = await Tickets.GetAsync(id);

        Assert.NotNull(ticket);
        Assert.InRange(ticket.CreatedAtUtc, before, DateTime.UtcNow.AddSeconds(5));
        // The whole product treats these as instants in UTC; a local-kind value would silently shift.
        Assert.Equal(DateTimeKind.Utc, ticket.CreatedAtUtc.Kind);
    }

    [RequiresDatabaseFact]
    public async Task Listing_users_materialises_every_column()
    {
        // This is the exact call that returned 500 in the first live run.
        await db.ResetAsync();
        await Users.CreateAsync(new UserCreate("Aysel Məmmədova", "aysel@test.local", UserRole.Agent, "DSL"));

        var users = await Users.ListAsync(null);

        Assert.Single(users);
        Assert.Equal(DateTimeKind.Utc, users[0].CreatedAtUtc.Kind);
    }

    [RequiresDatabaseFact]
    public async Task A_null_timestamp_stays_null()
    {
        await db.ResetAsync();
        var id = await Tickets.CreateAsync(NewTicket("Open ticket", "Not resolved."), null);

        var ticket = await Tickets.GetAsync(id);

        Assert.Null(ticket!.ResolvedAtUtc);
        Assert.Null(ticket.SlaDueAtUtc);
    }

    [RequiresDatabaseFact]
    public async Task An_sla_deadline_survives_the_round_trip()
    {
        await db.ResetAsync();
        var due = DateTime.UtcNow.AddHours(4);

        var id = await Tickets.CreateAsync(NewTicket("Urgent", "Escalate."), due);
        var ticket = await Tickets.GetAsync(id);

        Assert.NotNull(ticket!.SlaDueAtUtc);
        Assert.Equal(due, ticket.SlaDueAtUtc.Value, TimeSpan.FromSeconds(1));
    }

    // ── Bug 2: plainto_tsquery ANDs every term, so a long query matched nothing ────────────────────

    [RequiresDatabaseFact]
    public async Task A_long_query_still_matches_a_ticket_that_shares_only_some_terms()
    {
        await db.ResetAsync();
        await SeedResolvedAsync(
            "Internet drops every evening around 20:00",
            "Connection dies each evening between 19:45 and 20:15, then returns on its own.",
            "DSLAM port was retraining under evening load. Reduced the profile from 17a to 8b.");

        // A whole customer complaint. Under AND semantics the archive ticket would have to contain
        // every one of these words, which no real ticket ever does.
        var matches = await Tickets.SearchResolvedByTextAsync(
            "Connection cuts out every night, comes back by itself. Every night at about the same " +
            "time we lose the connection for maybe half an hour and then it returns without anyone " +
            "doing anything at all on our side.", 5);

        Assert.NotEmpty(matches);
    }

    [RequiresDatabaseFact]
    public async Task Search_only_returns_tickets_that_actually_have_a_resolution()
    {
        await db.ResetAsync();
        await SeedResolvedAsync("Wi-Fi slow in the bedroom", "Weak signal at the far end.",
            "Moved 2.4 GHz to channel 11.");
        // Open, and resolved-but-empty: neither is knowledge worth surfacing.
        await Tickets.CreateAsync(NewTicket("Wi-Fi slow upstairs", "Weak signal upstairs."), null);
        var blank = await Tickets.CreateAsync(NewTicket("Wi-Fi slow in the hall", "Weak signal."), null);
        await Tickets.SetStatusAsync(blank, TicketStatus.Resolved, "");

        var matches = await Tickets.SearchResolvedByTextAsync("wifi signal weak slow", 10);

        Assert.All(matches, m => Assert.False(string.IsNullOrWhiteSpace(m.Resolution)));
        Assert.Single(matches);
    }

    [RequiresDatabaseFact]
    public async Task A_query_of_nothing_but_stopwords_returns_nothing_instead_of_erroring()
    {
        // to_tsquery('') raises a syntax error; the NULLIF guard turns it into an empty result.
        await db.ResetAsync();
        await SeedResolvedAsync("Something", "Anything", "A resolution.");

        Assert.Empty(await Tickets.SearchResolvedByTextAsync("the and or of it", 5));
        Assert.Empty(await Tickets.SearchResolvedByTextAsync("!!! ??? ...", 5));
    }

    [RequiresDatabaseFact]
    public async Task Customer_text_cannot_reach_the_tsquery_parser_as_operators()
    {
        // Everything goes through to_tsvector first, so tsquery operators arrive as lexemes.
        await db.ResetAsync();
        await SeedResolvedAsync("Router reboot", "Customer rebooted the router.", "Cleared the session.");

        var matches = await Tickets.SearchResolvedByTextAsync("router & ! | <-> ( ) reboot", 5);

        Assert.Single(matches);
    }

    // ── Bug 3: ts_rank_cd × 10 saturated at 1.0, so every score tied and ordering fell to the date ─

    [RequiresDatabaseFact]
    public async Task The_closest_ticket_ranks_first_even_though_it_is_the_oldest()
    {
        await db.ResetAsync();

        // The expected ticket is seeded FIRST, so it is the oldest. That matters: when every score
        // collapses to the same value, ORDER BY falls through to resolved_at_utc DESC and the newest
        // ticket wins. The decoys therefore have to be newer *and* share the query's vocabulary —
        // decoys that match nothing would leave a single result and prove nothing.
        var expected = await SeedResolvedAsync(
            "Phone line has loud static noise on every call",
            "Heavy crackling on all calls, incoming and outgoing.",
            "Foreign voltage and low insulation resistance on the B leg — water in the joint box. " +
            "Field team resealed the joint and replaced 12 m of drop wire. Crackling or a rustling " +
            "sound behind the voice is almost always moisture, not the customer's handset.");

        await SeedResolvedAsync("Line drops during heavy rain",
            "The connection is unstable whenever it rains hard.",
            "Water ingress at the street cabinet after heavy rain; the joint was resealed.");
        await SeedResolvedAsync("Customer cannot make outgoing calls",
            "Every call the customer places fails immediately.",
            "Account suspended for non-payment; calls restored once the customer paid.");
        await SeedResolvedAsync("Noise on the customer's handset",
            "Customer reports a buzzing sound while on a call.",
            "Faulty handset, replaced by the customer. The line itself tested clean.");

        var matches = await Tickets.SearchResolvedByTextAsync(
            "Callers tell me I sound like I am underwater. People I ring keep asking me to repeat " +
            "myself. They say there is a rustling behind my voice. It started after the heavy rain " +
            "last week and it happens whoever I call.", 5);

        // Several candidates must come back, or the ranking assertion below is vacuous.
        Assert.True(matches.Count > 1, $"expected competing candidates, got {matches.Count}");
        Assert.Equal(expected, matches[0].SourceTicketId);
        Assert.True(matches[0].Similarity > matches[1].Similarity,
            $"scores collapsed: {matches[0].Similarity} vs {matches[1].Similarity} — " +
            "distinct relevance must produce distinct scores, or ordering falls through to the date.");
    }

    [RequiresDatabaseFact]
    public async Task Relevance_is_graded_rather_than_collapsed_onto_one_value()
    {
        // The clamp that caused the original bug flattened everything above 0.1 onto exactly 1.0.
        // Three tickets with deliberately different overlap must come back with different scores;
        // seeding them near-identical would make equal scores the *correct* answer and prove nothing.
        await db.ResetAsync();
        await SeedResolvedAsync("Router keeps losing its connection every evening",
            "The connection drops every evening and the line is unstable for about an hour.",
            "Line profile reduced and SRA enabled; the evening drops stopped.");
        await SeedResolvedAsync("Connection is occasionally unstable",
            "Sometimes the connection wobbles.",
            "Reseated the cable at the wall socket.");
        await SeedResolvedAsync("Invoice query about a monthly charge",
            "Customer asks what the line rental covers.",
            "Explained the tariff; no fault found.");

        var matches = await Tickets.SearchResolvedByTextAsync(
            "The connection drops every evening and the line is unstable for about an hour.", 5);

        Assert.True(matches.Count >= 2, $"expected a field of candidates, got {matches.Count}");
        Assert.True(matches.Select(m => m.Similarity).Distinct().Count() > 1,
            $"every candidate scored {matches[0].Similarity} — the scale has saturated");
    }

    [RequiresDatabaseFact]
    public async Task Relevance_stays_inside_the_unit_range()
    {
        await db.ResetAsync();
        await SeedResolvedAsync("Internet drops every evening", "Drops each evening.",
            "Reduced the profile and enabled SRA on the port.");

        var matches = await Tickets.SearchResolvedByTextAsync("internet drops evening profile SRA port", 5);

        Assert.All(matches, m => Assert.InRange(m.Similarity, 0.0, 1.0));
    }

    [RequiresDatabaseFact]
    public async Task Search_is_backed_by_the_generated_column_and_its_index()
    {
        // Ranking correctness would survive a sequential scan; this is the part that keeps it fast.
        Assert.Equal(1, await db.ScalarAsync<int>(
            """
            SELECT count(*)::int FROM pg_indexes
            WHERE tablename = 'tickets' AND indexname = 'ix_tickets_search'
            """));
        Assert.True(await db.ScalarAsync<bool>(
            """
            SELECT attgenerated <> '' FROM pg_attribute
            WHERE attrelid = 'tickets'::regclass AND attname = 'search_tsv'
            """));
    }

    // ── Reference generation, pagination, status transitions ──────────────────────────────────────

    [RequiresDatabaseFact]
    public async Task References_are_sequential_and_unique()
    {
        await db.ResetAsync();
        var ids = new List<long>();
        for (var i = 0; i < 5; i++) ids.Add(await Tickets.CreateAsync(NewTicket($"T{i}", "body"), null));

        var references = new List<string>();
        foreach (var id in ids) references.Add((await Tickets.GetAsync(id))!.Reference);

        Assert.Equal(references.Count, references.Distinct().Count());
        Assert.All(references, r => Assert.Matches(@"^RD-\d{4}-\d{6}$", r));
        Assert.EndsWith("000001", references[0]);
        Assert.EndsWith("000005", references[4]);
    }

    [RequiresDatabaseFact]
    public async Task A_ticket_can_be_fetched_by_its_reference()
    {
        await db.ResetAsync();
        var id = await Tickets.CreateAsync(NewTicket("Findable", "body"), null);
        var reference = (await Tickets.GetAsync(id))!.Reference;

        Assert.Equal(id, (await Tickets.GetByReferenceAsync(reference))!.Id);
        Assert.Null(await Tickets.GetByReferenceAsync("RD-1999-000001"));
    }

    [RequiresDatabaseFact]
    public async Task Keyset_pagination_walks_every_ticket_exactly_once()
    {
        await db.ResetAsync();
        for (var i = 0; i < 12; i++) await Tickets.CreateAsync(NewTicket($"Ticket {i}", "body"), null);

        var seen = new List<long>();
        long? cursor = null;
        do
        {
            var page = await Tickets.ListAsync(new TicketFilter(Cursor: cursor, Limit: 5));
            seen.AddRange(page.Items.Select(t => t.Id));
            cursor = page.NextCursor;
            Assert.Equal(page.HasMore, cursor is not null);
        }
        while (cursor is not null);

        Assert.Equal(12, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal([.. seen.OrderByDescending(x => x)], seen);
    }

    [RequiresDatabaseFact]
    public async Task Filters_combine_rather_than_replace_each_other()
    {
        await db.ResetAsync();
        var agent = await Users.CreateAsync(new UserCreate("Agent", "agent@test.local", UserRole.Agent, null));
        var coordinator = await Users.CreateAsync(new UserCreate("Coord", "coord@test.local", UserRole.Coordinator, null));

        var assigned = await Tickets.CreateAsync(NewTicket("Assigned one", "body"), null);
        await Tickets.AssignAsync(assigned, agent, coordinator);
        await Tickets.CreateAsync(NewTicket("Untouched", "body"), null);

        var byAssignee = await Tickets.ListAsync(new TicketFilter(AssigneeId: agent));
        Assert.Equal(assigned, Assert.Single(byAssignee.Items).Id);

        // Assigning moves Open to Assigned, so this pair should exclude each other.
        Assert.Empty((await Tickets.ListAsync(new TicketFilter(Status: TicketStatus.Open, AssigneeId: agent))).Items);
    }

    [RequiresDatabaseFact]
    public async Task Search_in_the_list_filter_matches_the_reference_too()
    {
        await db.ResetAsync();
        var id = await Tickets.CreateAsync(NewTicket("Distinctive title", "body"), null);
        var reference = (await Tickets.GetAsync(id))!.Reference;

        Assert.Single((await Tickets.ListAsync(new TicketFilter(Search: "Distinctive"))).Items);
        Assert.Single((await Tickets.ListAsync(new TicketFilter(Search: reference))).Items);
    }

    [RequiresDatabaseFact]
    public async Task Assigning_moves_an_open_ticket_to_assigned_but_leaves_later_states_alone()
    {
        await db.ResetAsync();
        var agent = await Users.CreateAsync(new UserCreate("Agent", "a@test.local", UserRole.Agent, null));
        var coordinator = await Users.CreateAsync(new UserCreate("Coord", "c@test.local", UserRole.Coordinator, null));

        var fresh = await Tickets.CreateAsync(NewTicket("Fresh", "body"), null);
        await Tickets.AssignAsync(fresh, agent, coordinator);
        Assert.Equal(TicketStatus.Assigned, (await Tickets.GetAsync(fresh))!.Status);

        var working = await Tickets.CreateAsync(NewTicket("In progress", "body"), null);
        await Tickets.SetStatusAsync(working, TicketStatus.InProgress, null);
        await Tickets.AssignAsync(working, agent, coordinator);
        // Reassigning mid-flight must not drag the ticket backwards.
        Assert.Equal(TicketStatus.InProgress, (await Tickets.GetAsync(working))!.Status);
    }

    [RequiresDatabaseTheory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task Resolving_stamps_the_resolution_time(TicketStatus status)
    {
        await db.ResetAsync();
        var id = await Tickets.CreateAsync(NewTicket("To resolve", "body"), null);

        await Tickets.SetStatusAsync(id, status, "Fixed it.");

        var ticket = await Tickets.GetAsync(id);
        Assert.NotNull(ticket!.ResolvedAtUtc);
        Assert.Equal("Fixed it.", ticket.Resolution);
    }

    [RequiresDatabaseFact]
    public async Task A_status_change_without_a_resolution_keeps_the_existing_one()
    {
        await db.ResetAsync();
        var id = await Tickets.CreateAsync(NewTicket("Reopened", "body"), null);
        await Tickets.SetStatusAsync(id, TicketStatus.Resolved, "The original fix.");

        await Tickets.SetStatusAsync(id, TicketStatus.InProgress, null);

        Assert.Equal("The original fix.", (await Tickets.GetAsync(id))!.Resolution);
    }

    [RequiresDatabaseFact]
    public async Task Updating_a_ticket_that_does_not_exist_reports_failure()
    {
        await db.ResetAsync();
        Assert.False(await Tickets.SetStatusAsync(999_999, TicketStatus.Resolved, "x"));
        Assert.False(await Tickets.AssignAsync(999_999, 1, 1));
        Assert.Null(await Tickets.GetAsync(999_999));
    }

    [RequiresDatabaseFact]
    public async Task Counting_by_status_reflects_the_transitions()
    {
        await db.ResetAsync();
        await Tickets.CreateAsync(NewTicket("One", "body"), null);
        var second = await Tickets.CreateAsync(NewTicket("Two", "body"), null);
        await Tickets.SetStatusAsync(second, TicketStatus.Resolved, "done");

        Assert.Equal(1, await Tickets.CountByStatusAsync(TicketStatus.Open));
        Assert.Equal(1, await Tickets.CountByStatusAsync(TicketStatus.Resolved));
        Assert.Equal(0, await Tickets.CountByStatusAsync(TicketStatus.Closed));
    }
}
