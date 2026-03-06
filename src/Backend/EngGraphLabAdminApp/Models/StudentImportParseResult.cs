namespace EngGraphLabAdminApp.Models;

public sealed record StudentImportParseResult(
    bool Success,
    string GroupName,
    IReadOnlyList<StudentImportRow> Students,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
