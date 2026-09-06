using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ResolveDesk.Infrastructure.Ai;

/// <summary>
/// Reads a JSON object out of a chat completion.
///
/// Models — local ones especially — wrap JSON in a fenced block, prefix it with "Here is the JSON:",
/// or append a closing remark. Insisting on a clean response would make the feature flaky for exactly
/// the local-first setup this product is built around, so the first balanced <c>{…}</c> is extracted
/// instead, and a failed parse returns null rather than throwing.
/// </summary>
internal static class ModelJson
{
    public static T? Extract<T>(string? completion, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;

        var span = completion.AsSpan();
        var start = span.IndexOf('{');
        if (start < 0) return null;

        // Brace matching rather than a regex, so a JSON string containing braces cannot end the object
        // early. Quote state is tracked to keep braces inside strings from counting.
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < span.Length; i++)
        {
            var c = span[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}' && --depth == 0)
            {
                try
                {
                    return JsonSerializer.Deserialize(span[start..(i + 1)], typeInfo);
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        return null;
    }
}
