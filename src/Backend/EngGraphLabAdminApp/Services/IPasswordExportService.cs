using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public interface IPasswordExportService
{
    PasswordExportDescriptor Store(string groupName, IReadOnlyList<StudentImportRow> students);
    bool TryBuildFile(string token, PasswordExportFormat format, out PasswordExportFile? file, out string error);
}
