namespace EngGraphLabAdminApp.Models;

public sealed class TFlexProvisioningExecuteRequest
{
    public TFlexConnectionRequest Connection { get; set; } = new();
    public string GroupName { get; set; } = string.Empty;
    public IReadOnlyList<TFlexProvisioningStudent> Students { get; set; } = [];
    public int PlannedActions { get; set; }
    public bool AssignTasks { get; set; } = true;
}

public sealed class TFlexProvisioningStudent
{
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string PinCode { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
}

public sealed record TFlexProvisioningExecuteResult(
    bool Success,
    string Message,
    int PlannedActions,
    int ExecutedActions,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingDependencies);
