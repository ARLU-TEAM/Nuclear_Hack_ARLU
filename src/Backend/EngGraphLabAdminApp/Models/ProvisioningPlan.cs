namespace EngGraphLabAdminApp.Models;

public sealed record ProvisioningPlan(
    string GroupName,
    int StudentsCount,
    IReadOnlyList<StudentImportRow> Students,
    IReadOnlyList<ProvisioningAction> Actions);

public sealed record ProvisioningAction(
    string Step,
    string Target,
    string Details);

public sealed record ProvisioningExecutionResult(
    bool Success,
    string Message,
    int PlannedActions,
    int ExecutedActions,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics);
