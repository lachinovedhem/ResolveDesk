using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Mcp;

/// <summary>
/// Talks to a running ResolveDesk API. Going over HTTP rather than straight to the database means one
/// MCP binary can point at a laptop, staging or production instance, and every call goes through the
/// same authorization and validation the web clients do.
/// </summary>
public sealed class ResolveDeskClient(HttpClient http)
{
    /// <summary>The API emits camelCase with string enums; mirror that exactly on the way back in.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<SuggestionResult?> SearchResolutionsAsync(string title, string? description, int limit, CancellationToken ct) =>
        PostAsync<SuggestionResult>("/api/v1/knowledge/search",
            new { title, description, limit }, ct);

    public Task<SuggestionResult?> SuggestionsForTicketAsync(long id, int limit, CancellationToken ct) =>
        GetAsync<SuggestionResult>($"/api/v1/tickets/{id}/suggestions?limit={limit}", ct);

    public Task<Ticket?> GetTicketAsync(long id, CancellationToken ct) =>
        GetAsync<Ticket>($"/api/v1/tickets/{id}", ct);

    public Task<Page<Ticket>?> ListTicketsAsync(string? status, long? assigneeId, int limit, CancellationToken ct)
    {
        var query = new List<string> { $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (assigneeId is { } a) query.Add($"assigneeId={a}");
        return GetAsync<Page<Ticket>>($"/api/v1/tickets?{string.Join('&', query)}", ct);
    }

    public Task<List<TicketActivity>?> ActivitiesAsync(long ticketId, CancellationToken ct) =>
        GetAsync<List<TicketActivity>>($"/api/v1/tickets/{ticketId}/activities", ct);

    public Task<List<User>?> ListUsersAsync(string? role, CancellationToken ct) =>
        GetAsync<List<User>>(role is null ? "/api/v1/users" : $"/api/v1/users?role={Uri.EscapeDataString(role)}", ct);

    public Task<AiStatus?> AiStatusAsync(CancellationToken ct) =>
        GetAsync<AiStatus>("/api/v1/ai/status", ct);

    public Task<JsonElement> StatsAsync(CancellationToken ct) =>
        GetAsync<JsonElement>("/api/v1/stats", ct);

    public Task<JsonElement> AddCommentAsync(long ticketId, long? authorId, string body, CancellationToken ct) =>
        PostAsync<JsonElement>($"/api/v1/tickets/{ticketId}/comments", new { authorId, body }, ct);

    public Task<bool> AssignAsync(long ticketId, long assigneeId, long coordinatorId, CancellationToken ct) =>
        PostNoContentAsync($"/api/v1/tickets/{ticketId}/assign", new { assigneeId, coordinatorId }, ct);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        await ThrowIfFailedAsync(response, path, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, Json, ct);
        await ThrowIfFailedAsync(response, path, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<bool> PostNoContentAsync(string path, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, Json, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        await ThrowIfFailedAsync(response, path, ct);
        return true;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"ResolveDesk API {(int)response.StatusCode} for {path}: {(body.Length > 300 ? body[..300] : body)}");
    }
}
