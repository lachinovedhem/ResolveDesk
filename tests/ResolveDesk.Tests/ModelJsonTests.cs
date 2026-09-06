using System.Text.Json;
using System.Text.Json.Serialization;
using ResolveDesk.Infrastructure.Ai;

namespace ResolveDesk.Tests;

/// <summary>
/// The extractor exists because local models decorate their JSON. These cases are the decorations
/// actually observed from qwen2.5-coder, plus the ones that would silently corrupt a parse.
/// </summary>
internal sealed record Payload(
    [property: JsonPropertyName("difficulty")] int? Difficulty,
    [property: JsonPropertyName("summary")] string? Summary);

// The generator needs a top-level partial context; nesting it inside the test class silently
// produces nothing and the compiler then reports the abstract members as unimplemented.
[JsonSerializable(typeof(Payload))]
internal sealed partial class PayloadContext : JsonSerializerContext;

public sealed class ModelJsonTests
{
    private static Payload? Extract(string? text) => ModelJson.Extract(text, PayloadContext.Default.Payload);

    [Fact]
    public void Reads_a_bare_object()
    {
        var result = Extract("""{"difficulty": 3, "summary": "line noise"}""");
        Assert.Equal(3, result?.Difficulty);
        Assert.Equal("line noise", result?.Summary);
    }

    [Fact]
    public void Reads_through_a_code_fence()
    {
        var result = Extract("""
            Here is the JSON:
            ```json
            {"difficulty": 4, "summary": "needs a field visit"}
            ```
            Let me know if you need anything else!
            """);
        Assert.Equal(4, result?.Difficulty);
    }

    [Fact]
    public void Braces_inside_a_string_do_not_end_the_object()
    {
        // Naive brace counting stops at the first '}' and yields invalid JSON.
        var result = Extract("""{"difficulty": 2, "summary": "customer wrote {weird} braces"}""");
        Assert.Equal("customer wrote {weird} braces", result?.Summary);
    }

    [Fact]
    public void Escaped_quotes_inside_a_string_do_not_end_the_string()
    {
        var result = Extract("""{"difficulty": 1, "summary": "he said \"it broke\" again"}""");
        Assert.Equal("""he said "it broke" again""", result?.Summary);
    }

    [Fact]
    public void Nested_objects_are_matched_to_the_outer_brace()
    {
        var result = Extract("""{"difficulty": 5, "summary": "x", "extra": {"a": {"b": 1}}}""");
        Assert.Equal(5, result?.Difficulty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I cannot answer that.")]                  // no object at all
    [InlineData("{\"difficulty\": 3, \"summary\":")]        // truncated mid-object
    [InlineData("{\"difficulty\": \"not a number\"}")]      // right shape, wrong type
    public void Returns_null_rather_than_throwing(string? input)
    {
        // A failed parse must degrade the assessment, never take down the triage worker.
        Assert.Null(Extract(input));
    }

    [Fact]
    public void Unparseable_output_does_not_throw_even_for_deeply_unbalanced_text()
    {
        Assert.Null(Extract(new string('{', 500)));
    }
}
