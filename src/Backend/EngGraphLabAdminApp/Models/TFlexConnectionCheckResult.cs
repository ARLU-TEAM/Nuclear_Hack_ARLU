namespace EngGraphLabAdminApp.Models;

public sealed record TFlexConnectionCheckResult(
    bool Success,
    string Message,
    string? ServerVersion,
    bool? IsAdministrator,
    IReadOnlyList<string> MissingDependencies);
