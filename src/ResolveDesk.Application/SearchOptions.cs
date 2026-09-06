using Microsoft.Extensions.Configuration;

namespace ResolveDesk.Application;

/// <summary>
/// Settings for the lexical (keyword) half of the hybrid search. Separate from <see cref="AiOptions"/>
/// on purpose: keyword search is the path that still works when every AI feature is switched off.
/// </summary>
public sealed record SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>
    /// The PostgreSQL text-search configuration used to build both the indexed document vector and the
    /// query. <c>english</c> stems words and drops stopwords, so "cuts"/"cutting" match and "the" is
    /// ignored. Use <c>simple</c> for corpora Postgres has no stemmer for — it does no stemming and
    /// keeps stopwords, which is worse than nothing for English but correct for, say, Azerbaijani.
    ///
    /// Changing this after the schema exists requires a migration: the value is baked into the
    /// generated <c>tickets.search_tsv</c> column, because a generated column can only call an
    /// immutable expression and <c>to_tsvector(regconfig, text)</c> is immutable only for a literal
    /// configuration. That is the trade for an index-backed search instead of a sequential scan.
    /// </summary>
    public string TextSearchConfig { get; init; } = "english";

    public static SearchOptions Load(IConfiguration config)
    {
        var value = config.GetSection(SectionName)["TextSearchConfig"];
        return new SearchOptions
        {
            TextSearchConfig = Validate(value) ? value! : "english",
        };
    }

    /// <summary>
    /// The value is interpolated into DDL, so it is checked against the shape of an identifier before
    /// it goes anywhere near the database. Existence is verified separately, against
    /// <c>pg_ts_config</c>, at schema time — a name that passes this check but does not exist would
    /// otherwise fail every query rather than at startup.
    /// </summary>
    public static bool Validate(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 63
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}
