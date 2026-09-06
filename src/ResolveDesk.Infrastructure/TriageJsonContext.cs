using System.Text.Json.Serialization;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// Source-generated serialisation for the values stored in jsonb columns. The API has its own context
/// for the HTTP surface; this one exists so persistence does not depend on the web layer, and so the
/// Infrastructure project stays trim-safe on its own.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<RoutingCandidate>))]
internal sealed partial class TriageJsonContext : JsonSerializerContext;
