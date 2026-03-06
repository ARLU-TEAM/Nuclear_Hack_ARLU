using EngGraphLabAdminApp.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EngGraphLabAdminApp.Services;

public sealed class StudentTableParser : IStudentTableParser
{
    private static readonly string[] LastNameHeaders = ["фамилия", "lastname", "surname", "last"];
    private static readonly string[] FirstNameHeaders = ["имя", "firstname", "name", "first"];
    private static readonly string[] MiddleNameHeaders = ["отчество", "middlename", "patronymic", "middle"];
    private static readonly string[] LoginHeaders = ["логин", "login", "username", "user"];

    public async Task<StudentImportParseResult> ParseAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return new StudentImportParseResult(
                Success: false,
                GroupName: string.Empty,
                Students: [],
                Errors: ["Файл пустой."],
                Warnings: []);
        }

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        List<RawStudentRow> rawRows;
        List<string> parseWarnings = [];

        try
        {
            rawRows = extension switch
            {
                ".csv" => ParseCsv(bytes, parseWarnings),
                ".xml" => ParseXml(bytes, parseWarnings),
                ".xlsx" or ".xls" => throw new NotSupportedException("Excel не реализован в базовом каркасе. Добавьте пакет ClosedXML или OpenXML SDK."),
                _ => throw new NotSupportedException($"Формат {extension} не поддерживается. Поддерживаются: .csv, .xml."),
            };
        }
        catch (Exception ex)
        {
            return new StudentImportParseResult(
                Success: false,
                GroupName: Path.GetFileNameWithoutExtension(file.FileName),
                Students: [],
                Errors: [$"Ошибка разбора файла: {ex.Message}"],
                Warnings: parseWarnings);
        }

        var errors = new List<string>();
        var students = new List<StudentImportRow>();
        foreach (var row in rawRows)
        {
            var lastName = NormalizeText(row.LastName);
            var firstName = NormalizeText(row.FirstName);
            var middleName = NormalizeText(row.MiddleName);
            var login = NormalizeLogin(row.Login);

            if (string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(middleName) ||
                string.IsNullOrWhiteSpace(login))
            {
                errors.Add($"Строка {row.RowNumber}: обязательные поля не заполнены после нормализации.");
                continue;
            }

            students.Add(new StudentImportRow(
                LastName: lastName,
                FirstName: firstName,
                MiddleName: middleName,
                Login: login,
                PinCode: GeneratePin(),
                RowNumber: row.RowNumber));
        }

        var duplicateLogins = students
            .GroupBy(static s => s.Login, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToArray();

        if (duplicateLogins.Length > 0)
        {
            errors.Add($"В файле найдены дубли логинов: {string.Join(", ", duplicateLogins)}.");
        }

        var groupName = NormalizeGroupName(Path.GetFileNameWithoutExtension(file.FileName));
        if (string.IsNullOrWhiteSpace(groupName))
        {
            errors.Add("Не удалось определить имя группы по имени файла.");
        }

        return new StudentImportParseResult(
            Success: errors.Count == 0 && students.Count > 0,
            GroupName: groupName,
            Students: students,
            Errors: errors,
            Warnings: parseWarnings);
    }

    private static List<RawStudentRow> ParseCsv(byte[] bytes, ICollection<string> warnings)
    {
        var content = DecodeText(bytes, warnings);
        using var reader = new StringReader(content);

        string? headerLine = null;
        while ((headerLine = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(headerLine))
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("CSV не содержит заголовок.");
        }

        var separator = DetectSeparator(headerLine);
        var headers = ParseCsvLine(headerLine, separator);
        var map = BuildHeaderMap(headers);

        var rows = new List<RawStudentRow>();
        var rowNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line, separator);
            rows.Add(new RawStudentRow(
                LastName: GetField(fields, map.LastNameIndex),
                FirstName: GetField(fields, map.FirstNameIndex),
                MiddleName: GetField(fields, map.MiddleNameIndex),
                Login: GetField(fields, map.LoginIndex),
                RowNumber: rowNumber));
        }

        return rows;
    }

    private static List<RawStudentRow> ParseXml(byte[] bytes, ICollection<string> warnings)
    {
        var content = DecodeText(bytes, warnings);
        var document = XDocument.Parse(content, LoadOptions.None);

        var candidates = document
            .Descendants()
            .Where(static e => e.HasElements)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("XML не содержит элементов-строк.");
        }

        var groupedByName = candidates
            .GroupBy(static e => e.Name.LocalName)
            .OrderByDescending(static g => g.Count())
            .ToArray();

        IEnumerable<XElement> rows = [];
        foreach (var group in groupedByName)
        {
            var groupRows = group
                .Where(static e => HasAnyRequiredColumns(e))
                .ToArray();

            if (groupRows.Length > 0)
            {
                rows = groupRows;
                break;
            }
        }

        if (!rows.Any())
        {
            throw new InvalidOperationException("Не удалось найти строки с колонками Фамилия/Имя/Отчество/Логин.");
        }

        var result = new List<RawStudentRow>();
        var rowNumber = 1;
        foreach (var row in rows)
        {
            var elements = row.Elements().ToArray();
            result.Add(new RawStudentRow(
                LastName: GetElementByAliases(elements, LastNameHeaders),
                FirstName: GetElementByAliases(elements, FirstNameHeaders),
                MiddleName: GetElementByAliases(elements, MiddleNameHeaders),
                Login: GetElementByAliases(elements, LoginHeaders),
                RowNumber: rowNumber));
            rowNumber++;
        }

        return result;
    }

    private static bool HasAnyRequiredColumns(XElement element)
    {
        var names = element.Elements()
            .Select(static x => NormalizeHeader(x.Name.LocalName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return names.Overlaps(LastNameHeaders.Select(NormalizeHeader)) &&
               names.Overlaps(FirstNameHeaders.Select(NormalizeHeader)) &&
               names.Overlaps(MiddleNameHeaders.Select(NormalizeHeader)) &&
               names.Overlaps(LoginHeaders.Select(NormalizeHeader));
    }

    private static string GetElementByAliases(IEnumerable<XElement> elements, IEnumerable<string> aliases)
    {
        var aliasSet = aliases
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return elements
            .Where(e => aliasSet.Contains(NormalizeHeader(e.Name.LocalName)))
            .Select(static e => e.Value)
            .FirstOrDefault() ?? string.Empty;
    }

    private static HeaderMap BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var normalized = headers
            .Select((header, index) => new IndexedHeader(NormalizeHeader(header), index))
            .ToArray();

        return new HeaderMap(
            LastNameIndex: ResolveColumnIndex(normalized, LastNameHeaders, "Фамилия"),
            FirstNameIndex: ResolveColumnIndex(normalized, FirstNameHeaders, "Имя"),
            MiddleNameIndex: ResolveColumnIndex(normalized, MiddleNameHeaders, "Отчество"),
            LoginIndex: ResolveColumnIndex(normalized, LoginHeaders, "Логин"));
    }

    private static int ResolveColumnIndex(
        IEnumerable<IndexedHeader> normalizedHeaders,
        IReadOnlyCollection<string> aliases,
        string originalHeader)
    {
        var aliasSet = aliases.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var match = normalizedHeaders.FirstOrDefault(h => aliasSet.Contains(h.Header));
        if (match is null)
        {
            throw new InvalidOperationException($"Не найдена обязательная колонка: {originalHeader}.");
        }

        return match.Index;
    }

    private static string GetField(IReadOnlyList<string> fields, int index)
    {
        if (index < 0 || index >= fields.Count)
        {
            return string.Empty;
        }

        return fields[index];
    }

    private static IReadOnlyList<string> ParseCsvLine(string line, char separator)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == separator && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static char DetectSeparator(string headerLine)
    {
        var candidates = new[] { ';', ',', '\t' };
        return candidates
            .Select(separator => new { Separator = separator, Count = headerLine.Count(ch => ch == separator) })
            .OrderByDescending(static x => x.Count)
            .First().Separator;
    }

    private static string DecodeText(byte[] bytes, ICollection<string> warnings)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        var text = utf8.GetString(bytes);
        if (text.Contains('\uFFFD'))
        {
            warnings.Add("Входной файл не UTF-8, применена fallback-кодировка windows-1251.");
            return Encoding.GetEncoding(1251).GetString(bytes);
        }

        return text;
    }

    private static string NormalizeHeader(string value)
    {
        return Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s_-]+", string.Empty);
    }

    private static string NormalizeText(string value)
    {
        var trimmed = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return Regex.Replace(trimmed, @"[\p{C}]+", string.Empty);
    }

    private static string NormalizeLogin(string value)
    {
        var normalized = NormalizeText(value).ToLowerInvariant();
        return Regex.Replace(normalized, @"[^\p{L}\p{Nd}._-]", string.Empty);
    }

    private static string GeneratePin()
    {
        return RandomNumberGenerator.GetInt32(0, 100000).ToString("D5");
    }

    private static string NormalizeGroupName(string raw)
    {
        return NormalizeText(raw);
    }

    private sealed record RawStudentRow(
        string LastName,
        string FirstName,
        string MiddleName,
        string Login,
        int RowNumber);

    private sealed record HeaderMap(
        int LastNameIndex,
        int FirstNameIndex,
        int MiddleNameIndex,
        int LoginIndex);

    private sealed record IndexedHeader(string Header, int Index);
}
