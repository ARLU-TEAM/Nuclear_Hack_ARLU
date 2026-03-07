namespace EngGraphLabAdminApp.Models;

public enum PasswordExportFormat
{
    Csv,
    Xlsx
}

public sealed record PasswordExportDescriptor(
    string Token,
    string GroupName,
    int StudentsCount,
    DateTimeOffset GeneratedAtUtc,
    string CsvUrl,
    string XlsxUrl);

public sealed record PasswordExportFile(
    byte[] Content,
    string ContentType,
    string FileName);
