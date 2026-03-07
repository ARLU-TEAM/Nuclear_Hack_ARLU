using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public interface IProvisioningService
{
    ProvisioningPlan BuildPlan(string groupName, IReadOnlyList<StudentImportRow> students, bool includeTaskDistribution);
    Task<ProvisioningExecutionResult> ExecuteFoundationAsync(ProvisioningPlan plan, bool includeTaskDistribution, CancellationToken cancellationToken);
}
