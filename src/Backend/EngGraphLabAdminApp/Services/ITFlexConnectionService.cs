using EngGraphLabAdminApp.Models;
using EngGraphLabAdminApp.Options;

namespace EngGraphLabAdminApp.Services;

public interface ITFlexConnectionService
{
    Task<TFlexConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken);
    Task<TFlexConnectionCheckResult> CheckConnectionAsync(TFlexOptions options, CancellationToken cancellationToken);
}
