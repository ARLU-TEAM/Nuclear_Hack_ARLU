using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public interface IProvisioningService
{
    ProvisioningPlan BuildPlan(string groupName, IReadOnlyList<StudentImportRow> students);
    Task<ProvisioningExecutionResult> ExecuteFoundationAsync(ProvisioningPlan plan, CancellationToken cancellationToken);
}
