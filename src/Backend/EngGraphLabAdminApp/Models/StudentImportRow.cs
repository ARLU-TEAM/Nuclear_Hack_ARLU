namespace EngGraphLabAdminApp.Models;

public sealed record StudentImportRow(
    string LastName,
    string FirstName,
    string MiddleName,
    string Login,
    string PinCode,
    int RowNumber)
{
    public string FolderName => $"{LastName} {FirstLetter(FirstName)} {FirstLetter(MiddleName)}".Trim();
    public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }.Where(static x => !string.IsNullOrWhiteSpace(x)));

    private static string FirstLetter(string value) => string.IsNullOrEmpty(value) ? string.Empty : value[..1];
}
