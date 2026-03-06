using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public interface IStudentTableParser
{
    Task<StudentImportParseResult> ParseAsync(IFormFile file, CancellationToken cancellationToken);
}
