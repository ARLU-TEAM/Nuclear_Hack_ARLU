using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EngGraphLabAdminApp.Models;

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
                ".xlsx" => ParseXlsx(bytes),
                ".xls" => throw new NotSupportedException("Формат .xls (старый бинарный Excel) не поддерживается. Сохраните файл как .xlsx, .csv или .xml."),
                _ => throw new NotSupportedException($"Формат {extension} не поддерживается. Поддерживаются: .csv, .xml, .xlsx.")
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

    private static List<RawStudentRow> ParseXlsx(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);

        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("В .xlsx отсутствует xl/workbook.xml.");
        var workbook = LoadXml(workbookEntry);

        XNamespace wbNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var firstSheet = workbook
            .Element(wbNs + "sheets")?
            .Elements(wbNs + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidOperationException("В .xlsx отсутствуют листы.");

        var relationshipId = firstSheet.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            throw new InvalidOperationException("Не удалось определить relationship id первого листа.");
        }

        var relEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("В .xlsx отсутствует xl/_rels/workbook.xml.rels.");
        var relDoc = LoadXml(relEntry);
        XNamespace relDocNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationship = relDoc
            .Elements(relDocNs + "Relationship")
            .FirstOrDefault(x => string.Equals(x.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal));

        if (relationship is null)
        {
            throw new InvalidOperationException("Не найден relationship для первого листа.");
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("Relationship листа не содержит Target.");
        }

        var normalizedTarget = target.Replace('\\', '/').TrimStart('/');
        var sheetPath = normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalizedTarget
            : $"xl/{normalizedTarget}";

        var sheetEntry = archive.GetEntry(sheetPath)
            ?? throw new InvalidOperationException($"Не найден XML листа: {sheetPath}.");

        var sharedStrings = ReadSharedStrings(archive);
        var sheet = LoadXml(sheetEntry);

        var rows = sheet
            .Elements(wbNs + "sheetData")
            .Elements(wbNs + "row")
            .ToArray()
            ?? [];

        if (rows.Length == 0)
        {
            throw new InvalidOperationException("Лист .xlsx пустой.");
        }

        var parsedRows = rows
            .Select(row => ParseWorksheetRow(row, wbNs, sharedStrings))
            .Where(static x => x.Values.Count > 0)
            .ToArray();

        if (parsedRows.Length == 0)
        {
            throw new InvalidOperationException("В листе .xlsx отсутствуют строки данных.");
        }

        var headerRow = parsedRows[0];
        var headers = BuildHeaderArray(headerRow.Values);
        var map = BuildHeaderMap(headers);

        var result = new List<RawStudentRow>();
        for (var i = 1; i < parsedRows.Length; i++)
        {
            var row = parsedRows[i];
            result.Add(new RawStudentRow(
                LastName: GetFieldByColumn(row.Values, map.LastNameIndex),
                FirstName: GetFieldByColumn(row.Values, map.FirstNameIndex),
                MiddleName: GetFieldByColumn(row.Values, map.MiddleNameIndex),
                Login: GetFieldByColumn(row.Values, map.LoginIndex),
                RowNumber: row.RowNumber));
        }

        return result;
    }

    private static XElement LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XElement.Load(stream, LoadOptions.None);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        var root = LoadXml(sharedStringsEntry);
        XNamespace wbNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        return root
            .Elements(wbNs + "si")
            .Select(si => string.Concat(si.Descendants(wbNs + "t").Select(static t => t.Value)))
            .ToArray();
    }

    private static ParsedWorksheetRow ParseWorksheetRow(XElement row, XNamespace wbNs, IReadOnlyList<string> sharedStrings)
    {
        var values = new Dictionary<int, string>();
        var rowNumber = ParseRowNumber(row.Attribute("r")?.Value);

        foreach (var cell in row.Elements(wbNs + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var columnIndex = ParseColumnIndex(reference);
            if (columnIndex < 0)
            {
                continue;
            }

            var type = cell.Attribute("t")?.Value;
            var value = ReadCellValue(cell, wbNs, type, sharedStrings);
            values[columnIndex] = value;
        }

        return new ParsedWorksheetRow(rowNumber, values);
    }

    private static int ParseRowNumber(string? rowReference)
    {
        if (string.IsNullOrWhiteSpace(rowReference))
        {
            return 0;
        }

        var digits = new string(rowReference.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber)
            ? rowNumber
            : 0;
    }

    private static int ParseColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return -1;
        }

        var letters = new string(cellReference.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        if (letters.Length == 0)
        {
            return -1;
        }

        var value = 0;
        foreach (var ch in letters)
        {
            value = (value * 26) + (ch - 'A' + 1);
        }

        return value - 1;
    }

    private static string ReadCellValue(XElement cell, XNamespace wbNs, string? cellType, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.Element(wbNs + "v")?.Value ?? string.Empty;
        if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                index >= 0 &&
                index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }

            return string.Empty;
        }

        if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(wbNs + "t").Select(static t => t.Value));
        }

        if (string.Equals(cellType, "b", StringComparison.OrdinalIgnoreCase))
        {
            return value == "1" ? "true" : "false";
        }

        return value;
    }

    private static IReadOnlyList<string> BuildHeaderArray(IReadOnlyDictionary<int, string> columns)
    {
        if (columns.Count == 0)
        {
            return [];
        }

        var maxIndex = columns.Keys.Max();
        var headers = new string[maxIndex + 1];
        foreach (var pair in columns)
        {
            headers[pair.Key] = pair.Value;
        }

        return headers;
    }

    private static string GetFieldByColumn(IReadOnlyDictionary<int, string> columns, int index)
    {
        return columns.TryGetValue(index, out var value) ? value : string.Empty;
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

    private sealed record ParsedWorksheetRow(int RowNumber, IReadOnlyDictionary<int, string> Values);
}
