using Microsoft.Extensions.Configuration;
using ResolveDesk.Application;

namespace ResolveDesk.Tests;

public sealed class SearchOptionsTests
{
    private static SearchOptions Load(params (string Key, string? Value)[] settings) =>
        SearchOptions.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Theory]
    [InlineData("english")]
    [InlineData("simple")]
    [InlineData("norwegian")]
    [InlineData("pg_catalog_like_name_2")]
    public void Accepts_identifier_shaped_names(string value) =>
        Assert.True(SearchOptions.Validate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("English")]                       // the DDL is lower-case; reject rather than guess
    [InlineData("english; DROP TABLE tickets")]   // the reason this check exists
    [InlineData("english'")]
    [InlineData("\"english\"")]
    [InlineData("en glish")]
    [InlineData("english--")]
    public void Rejects_anything_that_is_not_a_plain_identifier(string? value) =>
        Assert.False(SearchOptions.Validate(value));

    [Fact]
    public void Rejects_names_longer_than_an_identifier_can_be() =>
        Assert.False(SearchOptions.Validate(new string('a', 64)));

    [Fact]
    public void Defaults_to_english_when_unset() =>
        Assert.Equal("english", Load().TextSearchConfig);

    [Fact]
    public void Falls_back_to_english_rather_than_carrying_a_rejected_value_into_ddl() =>
        Assert.Equal("english", Load(("Search:TextSearchConfig", "english; DROP TABLE tickets")).TextSearchConfig);

    [Fact]
    public void Reads_a_configured_value() =>
        Assert.Equal("simple", Load(("Search:TextSearchConfig", "simple")).TextSearchConfig);
}

public sealed class AiOptionsTests
{
    private static AiOptions Load(params (string Key, string? Value)[] settings) =>
        AiOptions.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public void MinSimilarity_defaults_to_the_measured_value() =>
        Assert.Equal(0.50, Load().MinSimilarity);

    [Fact]
    public void MinSimilarity_parses_with_a_dot_regardless_of_machine_culture()
    {
        // The machine this was built on formats decimals with a comma, so "0.42" read under the
        // ambient culture would fail to parse and silently fall back to the default — a threshold
        // quietly wrong by 0.08 with nothing in the logs to say so.
        var previous = Thread.CurrentThread.CurrentCulture;
        var commaLocale = new System.Globalization.CultureInfo("de-DE");
        Assert.Equal(",", commaLocale.NumberFormat.NumberDecimalSeparator);  // the premise itself

        Thread.CurrentThread.CurrentCulture = commaLocale;
        try
        {
            Assert.Equal(0.42, Load(("Ai:MinSimilarity", "0.42")).MinSimilarity);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("1.5", 1.0)]
    [InlineData("-0.2", 0.0)]
    public void MinSimilarity_is_clamped_to_the_range_a_cosine_can_occupy(string configured, double expected) =>
        Assert.Equal(expected, Load(("Ai:MinSimilarity", configured)).MinSimilarity);

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    public void MinSimilarity_falls_back_when_unparseable(string configured) =>
        Assert.Equal(0.50, Load(("Ai:MinSimilarity", configured)).MinSimilarity);

    [Fact]
    public void Disabled_by_default_so_the_product_runs_without_any_model() =>
        Assert.False(Load().Enabled);

    [Fact]
    public void Chat_and_embedding_are_configured_independently()
    {
        var options = Load(
            ("Ai:Enabled", "true"),
            ("Ai:Chat:Provider", "Gemini"),
            ("Ai:Embedding:Provider", "Ollama"));

        Assert.True(options.ChatEnabled);
        Assert.True(options.EmbeddingEnabled);
        Assert.NotEqual(options.Chat.Provider, options.Embedding.Provider);
    }

    [Fact]
    public void A_provider_of_none_disables_only_that_capability()
    {
        var options = Load(
            ("Ai:Enabled", "true"),
            ("Ai:Chat:Provider", "None"),
            ("Ai:Embedding:Provider", "Ollama"));

        Assert.False(options.ChatEnabled);
        Assert.True(options.EmbeddingEnabled);
    }

    [Fact]
    public void The_master_switch_overrides_both()
    {
        var options = Load(
            ("Ai:Enabled", "false"),
            ("Ai:Chat:Provider", "Ollama"),
            ("Ai:Embedding:Provider", "Ollama"));

        Assert.False(options.ChatEnabled);
        Assert.False(options.EmbeddingEnabled);
    }
}
