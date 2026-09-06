namespace ResolveDesk.Core;

/// <summary>A team member. Coordinators route incoming tickets; agents resolve them.</summary>
public sealed record User(
    long Id,
    string FullName,
    string Email,
    UserRole Role,
    string? Skills,        // comma-separated tags used for routing suggestions
    bool IsActive,
    DateTime CreatedAtUtc);
