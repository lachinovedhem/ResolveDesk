using ResolveDesk.Core;
using ResolveDesk.Infrastructure;

namespace ResolveDesk.Tests;

/// <summary>
/// Reciprocal rank fusion is the join between two lists whose scores mean different things — a cosine
/// similarity and a ts_rank_cd relevance. These tests pin the properties that make that safe.
/// </summary>
public sealed class FusionTests
{
    private static ResolutionSuggestion Hit(long id, double similarity, string kind) =>
        new(id, $"RD-2026-{id:000000}", $"Ticket {id}", "resolution text", similarity, kind);

    private static List<long> Order(List<ResolutionSuggestion> fused) =>
        [.. fused.Select(f => f.SourceTicketId)];

    [Fact]
    public void A_ticket_found_by_both_lists_outranks_one_found_by_either()
    {
        // 2 is second in both lists; 1 and 3 are first in one list each. Agreement should win.
        var semantic = new List<ResolutionSuggestion> { Hit(1, 0.90, "semantic"), Hit(2, 0.80, "semantic") };
        var lexical = new List<ResolutionSuggestion> { Hit(3, 0.50, "keyword"), Hit(2, 0.40, "keyword") };

        Assert.Equal(2, Order(SuggestionService.Fuse(semantic, lexical))[0]);
    }

    [Fact]
    public void Fusion_ignores_the_scales_of_the_two_score_systems()
    {
        // The keyword hit's 0.04 is a decisive first place; the semantic hit's 0.55 is a weak one.
        // Summing the scores would bury the keyword hit — fusing by rank keeps them comparable.
        var semantic = new List<ResolutionSuggestion> { Hit(1, 0.55, "semantic") };
        var lexical = new List<ResolutionSuggestion> { Hit(1, 0.04, "keyword") };

        var fused = SuggestionService.Fuse(semantic, lexical);

        Assert.Single(fused);
        Assert.Equal(1, fused[0].SourceTicketId);
    }

    [Fact]
    public void A_ticket_in_both_lists_is_labelled_hybrid()
    {
        var fused = SuggestionService.Fuse(
            [Hit(1, 0.90, "semantic")],
            [Hit(1, 0.30, "keyword")]);

        Assert.Equal("hybrid", fused[0].MatchKind);
    }

    [Fact]
    public void A_ticket_in_one_list_keeps_that_lists_label()
    {
        var fused = SuggestionService.Fuse(
            [Hit(1, 0.90, "semantic")],
            [Hit(2, 0.30, "keyword")]);

        Assert.Equal("semantic", fused.Single(f => f.SourceTicketId == 1).MatchKind);
        Assert.Equal("keyword", fused.Single(f => f.SourceTicketId == 2).MatchKind);
    }

    [Fact]
    public void The_higher_similarity_copy_is_the_one_reported()
    {
        // Both lists carry the same ticket with different scores; the semantic number is the
        // meaningful one to show a coordinator.
        var fused = SuggestionService.Fuse(
            [Hit(1, 0.72, "semantic")],
            [Hit(1, 0.04, "keyword")]);

        Assert.Equal(0.72, fused[0].Similarity);
    }

    [Fact]
    public void Duplicates_across_lists_are_merged_not_repeated()
    {
        var fused = SuggestionService.Fuse(
            [Hit(1, 0.9, "semantic"), Hit(2, 0.8, "semantic")],
            [Hit(1, 0.4, "keyword"), Hit(2, 0.3, "keyword")]);

        Assert.Equal(2, fused.Count);
        Assert.Equal(fused.Count, fused.Select(f => f.SourceTicketId).Distinct().Count());
    }

    [Fact]
    public void Rank_order_within_a_single_list_is_preserved()
    {
        var semantic = new List<ResolutionSuggestion> { Hit(1, 0.9, "semantic"), Hit(2, 0.8, "semantic"), Hit(3, 0.7, "semantic") };

        Assert.Equal([1, 2, 3], Order(SuggestionService.Fuse(semantic, [])));
    }

    [Fact]
    public void Either_list_may_be_empty()
    {
        Assert.Empty(SuggestionService.Fuse([], []));
        Assert.Single(SuggestionService.Fuse([Hit(1, 0.9, "semantic")], []));
        Assert.Single(SuggestionService.Fuse([], [Hit(1, 0.1, "keyword")]));
    }
}
