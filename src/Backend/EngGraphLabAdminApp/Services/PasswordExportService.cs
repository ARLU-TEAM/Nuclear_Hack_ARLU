using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public sealed class PasswordExportService : IPasswordExportService
{
    private const int MaxSnapshots = 500;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);

    public PasswordExportDescriptor Store(string groupName, IReadOnlyList<StudentImportRow> students)
    {
        Cleanup();

        var token = Guid.NewGuid().ToString("N");
        var normalizedGroup = string.IsNullOrWhiteSpace(groupName) ? "group" : groupName.Trim();
        var rows = students
            .Select(static s => new PasswordRow(
                s.LastName,
                s.FirstName,
                s.MiddleName,
                s.Login,
                s.PinCode))
            .ToArray();

        var snapshot = new Snapshot(normalizedGroup, DateTimeOffset.UtcNow, rows);
        _snapshots[token] = snapshot;

        return new PasswordExportDescriptor(
            Token: token,
            GroupName: normalizedGroup,
            StudentsCount: rows.Length,
            GeneratedAtUtc: snapshot.CreatedAtUtc,
            CsvUrl: $"/api/provisioning/passwords/{token}?format=csv",
            XlsxUrl: $"/api/provisioning/passwords/{token}?format=xlsx");
    }

    public bool TryBuildFile(string token, PasswordExportFormat format, out PasswordExportFile? file, out string error)
    {
        file = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            error = "Token is required.";
            return false;
        }

        if (!_snapshots.TryGetValue(token, out var snapshot))
        {
            error = "Password file token not found or expired.";
            return false;
        }

        if (DateTimeOffset.UtcNow - snapshot.CreatedAtUtc > MaxAge)
        {
            _snapshots.TryRemove(token, out _);
            error = "Password file token expired.";
            return false;
        }

        var safeGroup = SanitizeFileName(snapshot.GroupName);
        var stamp = snapshot.CreatedAtUtc.ToString("yyyyMMdd_HHmmss");
        switch (format)
        {
            case PasswordExportFormat.Csv:
                file = new PasswordExportFile(
                    Content: BuildCsv(snapshot.Rows),
                    ContentType: "text/csv; charset=utf-8",
                    FileName: $"{safeGroup}_passwords_{stamp}.csv");
                return true;

            case PasswordExportFormat.Xlsx:
                file = new PasswordExportFile(
                    Content: BuildXlsx(snapshot.Rows),
                    ContentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    FileName: $"{safeGroup}_passwords_{stamp}.xlsx");
                return true;

            default:
                error = "Unsupported format.";
                return false;
        }
    }

    private static byte[] BuildCsv(IReadOnlyList<PasswordRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Фамилия;Имя;Отчество;Логин;Пароль");
        foreach (var row in rows)
        {
            sb.Append(EscapeCsv(row.LastName)).Append(';')
                .Append(EscapeCsv(row.FirstName)).Append(';')
                .Append(EscapeCsv(row.MiddleName)).Append(';')
                .Append(EscapeCsv(row.Login)).Append(';')
                .Append(EscapeCsv(row.Password))
                .AppendLine();
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildXlsx(IReadOnlyList<PasswordRow> rows)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);

            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Пароли" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);

            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildSheetXml(rows));
        }

        return memory.ToArray();
    }

    private static string BuildSheetXml(IReadOnlyList<PasswordRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
            """);

        var header = new[] { "Фамилия", "Имя", "Отчество", "Логин", "Пароль" };
        AppendRow(sb, 1, header);

        var rowIndex = 2;
        foreach (var row in rows)
        {
            AppendRow(sb, rowIndex, new[] { row.LastName, row.FirstName, row.MiddleName, row.Login, row.Password });
            rowIndex++;
        }

        sb.Append("""
              </sheetData>
            </worksheet>
            """);

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowIndex, IReadOnlyList<string> values)
    {
        sb.Append("<row r=\"").Append(rowIndex).Append("\">");
        for (var i = 0; i < values.Count; i++)
        {
            var cellRef = ColumnName(i) + rowIndex;
            sb.Append("<c r=\"").Append(cellRef).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(EscapeXml(values[i]))
                .Append("</t></is></c>");
        }

        sb.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var value = index + 1;
        var chars = new StringBuilder();
        while (value > 0)
        {
            value--;
            chars.Insert(0, (char)('A' + (value % 26)));
            value /= 26;
        }

        return chars.ToString();
    }

    private static string EscapeCsv(string value)
    {
        var v = value ?? string.Empty;
        if (v.IndexOfAny([';', '"', '\r', '\n']) >= 0)
        {
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        return v;
    }

    private static string EscapeXml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string SanitizeFileName(string value)
    {
        var result = string.IsNullOrWhiteSpace(value) ? "group" : value.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(ch, '_');
        }

        return result;
    }

    private void Cleanup()
    {
        if (_snapshots.Count <= MaxSnapshots)
        {
            return;
        }

        var border = DateTimeOffset.UtcNow - MaxAge;
        foreach (var pair in _snapshots)
        {
            if (pair.Value.CreatedAtUtc < border)
            {
                _snapshots.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Snapshot(
        string GroupName,
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<PasswordRow> Rows);

    private sealed record PasswordRow(
        string LastName,
        string FirstName,
        string MiddleName,
        string Login,
        string Password);
}
