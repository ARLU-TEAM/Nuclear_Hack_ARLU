namespace EngGraphLabAdminApp.Models;

public sealed record ProvisioningPlan(
    string GroupName,
    int StudentsCount,
    IReadOnlyList<ProvisioningAction> Actions);

public sealed record ProvisioningAction(
    string Step,
    string Target,
    string Details);

public sealed record ProvisioningExecutionResult(
    bool Success,
    string Message,
    int PlannedActions,
    int ExecutedActions);
