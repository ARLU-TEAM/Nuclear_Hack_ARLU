using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TFlex.DOCs.Model;
using TFlex.DOCs.Model.Macros;
using TFlex.DOCs.Model.References.Files;
using TFlex.DOCs.Model.References.Users;

namespace EngGraphLabProvisioningMacro;

public sealed class Macro : MacroProvider
{
    private const string DefaultCsvPath = @"C:\Temp\M25-123.csv";

    private const string FilesRootRu = "\u0424\u0430\u0439\u043b\u044b";         // Файлы
    private const string StudentsRootRu = "\u0421\u0442\u0443\u0434\u0435\u043d\u0442\u044b";  // Студенты
    private const string AssignmentsRu = "\u0417\u0430\u0434\u0430\u043d\u0438\u044f";   // Задания
    private const string AssignmentsTypoRu = "\u0417\u0434\u0430\u043d\u0438\u044f"; // Здания
    private const string TeachersRu = "\u041f\u0440\u0435\u043f\u043e\u0434\u0430\u0432\u0430\u0442\u0435\u043b\u0438"; // Преподаватели

    private static readonly string[] FilesRootCandidates = { FilesRootRu, "Files" };
    private static readonly string[] AssignmentsRootCandidates =
    {
        FilesRootRu + "/" + AssignmentsRu,
        AssignmentsRu,
        FilesRootRu + "/" + AssignmentsTypoRu,
        AssignmentsTypoRu
    };

    private static readonly Regex WorkFolderRegex = new Regex("^\\s*\\u0420\\u0430\\u0431\\u043e\\u0442\\u0430\\s*\\d+\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Macro(MacroContext context) : base(context)
    {
    }

    public override void Run()
    {
        var logs = new List<string>();

        try
        {
            var csvPath = ResolveCsvPath();
            logs.Add("CSV: " + csvPath);

            var students = ReadStudents(csvPath);
            if (students.Count == 0)
            {
                Error("Provisioning", "CSV does not contain students.");
                return;
            }

            var groupName = Path.GetFileNameWithoutExtension(csvPath)?.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                Error("Provisioning", "Cannot detect group name from CSV filename.");
                return;
            }

            var userReference = new UserReference(Context.Connection);
            var fileReference = new FileReference(Context.Connection);

            var studentsTopGroup = EnsureTopGroup(userReference, StudentsRootRu, "Students groups", logs);
            var group = EnsureChildGroup(userReference, studentsTopGroup, groupName, logs);
            EnsureTopGroup(userReference, TeachersRu, "Teachers group", logs);

            var filesRoot = EnsureFilesRootFolder(fileReference, logs);
            var studentsRoot = EnsureChildFolder(fileReference, filesRoot, StudentsRootRu, logs);
            var groupFolder = EnsureChildFolder(fileReference, studentsRoot, groupName, logs);

            var assignmentState = BuildAssignmentState(fileReference, FindAssignmentsRoot(fileReference, filesRoot));
            if (assignmentState.WorkFolders.Count == 0)
            {
                logs.Add("Assignments source folders not found. Only structure will be created.");
            }

            foreach (var student in students)
            {
                var user = EnsureUser(userReference, student, logs);
                EnsureMembership(userReference, group, user, logs);

                var studentFolder = EnsureChildFolder(fileReference, groupFolder, student.FolderName, logs);
                var studentTasksFolder = EnsureChildFolder(fileReference, studentFolder, AssignmentsRu, logs);

                AssignTasksIfPossible(fileReference, assignmentState, studentTasksFolder, logs);
            }

            Message("Provisioning", "Done." + Environment.NewLine + string.Join(Environment.NewLine, logs));
        }
        catch (Exception ex)
        {
            Error("Provisioning error", ex.GetType().Name + ": " + ex.Message);
            throw;
        }
    }

    private static string ResolveCsvPath()
    {
        var env = Environment.GetEnvironmentVariable("TFLEX_STUDENTS_CSV");
        var path = string.IsNullOrWhiteSpace(env) ? DefaultCsvPath : env.Trim();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CSV not found. Set TFLEX_STUDENTS_CSV or place file at default path.", path);
        }

        return path;
    }

    private static List<StudentRow> ReadStudents(string path)
    {
        var rawLines = File.ReadAllLines(path, Encoding.UTF8)
            .Select(static x => x?.Trim() ?? string.Empty)
            .Where(static x => x.Length > 0)
            .ToArray();

        if (rawLines.Length < 2)
        {
            return new List<StudentRow>();
        }

        var delimiter = rawLines[0].Contains(';') ? ';' : ',';
        var headers = SplitLine(rawLines[0], delimiter)
            .Select(static x => (x ?? string.Empty).Trim().ToLowerInvariant())
            .ToArray();

        int idxLast = FindHeader(headers, "lastname", "last_name", "surname");
        int idxFirst = FindHeader(headers, "firstname", "first_name", "name");
        int idxMiddle = FindHeader(headers, "middlename", "middle_name", "patronymic");
        int idxLogin = FindHeader(headers, "login", "username", "user");
        int idxPin = FindHeader(headers, "pincode", "pin", "password");

        if (idxLast < 0) idxLast = 0;
        if (idxFirst < 0) idxFirst = 1;
        if (idxMiddle < 0) idxMiddle = 2;
        if (idxLogin < 0) idxLogin = 3;

        var result = new List<StudentRow>();
        for (int i = 1; i < rawLines.Length; i++)
        {
            var cells = SplitLine(rawLines[i], delimiter);

            string lastName = GetCell(cells, idxLast);
            string firstName = GetCell(cells, idxFirst);
            string middleName = GetCell(cells, idxMiddle);
            string login = NormalizeLogin(GetCell(cells, idxLogin));
            string pinCode = idxPin >= 0 ? GetCell(cells, idxPin) : string.Empty;

            if (string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(firstName) ||
                string.IsNullOrWhiteSpace(login))
            {
                continue;
            }

            result.Add(new StudentRow
            {
                LastName = lastName,
                FirstName = firstName,
                MiddleName = middleName,
                Login = login,
                PinCode = string.IsNullOrWhiteSpace(pinCode) ? GeneratePinCode(login) : pinCode,
                FolderName = BuildFolderName(lastName, firstName, middleName)
            });
        }

        return result
            .GroupBy(static x => x.Login, StringComparer.OrdinalIgnoreCase)
            .Select(static g => g.First())
            .ToList();
    }

    private static string[] SplitLine(string line, char delimiter)
    {
        return (line ?? string.Empty).Split(new[] { delimiter }, StringSplitOptions.None);
    }

    private static int FindHeader(IReadOnlyList<string> headers, params string[] variants)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            var h = headers[i];
            for (int j = 0; j < variants.Length; j++)
            {
                if (string.Equals(h, variants[j], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string GetCell(IReadOnlyList<string> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
        {
            return string.Empty;
        }

        return (cells[index] ?? string.Empty).Trim();
    }

    private static string NormalizeLogin(string login)
    {
        return (login ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string BuildFolderName(string lastName, string firstName, string middleName)
    {
        return (lastName ?? string.Empty).Trim() +
               " " + FirstLetter(firstName) +
               " " + FirstLetter(middleName);
    }

    private static string FirstLetter(string value)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length == 0 ? string.Empty : v.Substring(0, 1).ToUpperInvariant();
    }

    private static string GeneratePinCode(string seed)
    {
        unchecked
        {
            var hash = (seed ?? string.Empty).ToLowerInvariant().GetHashCode();
            var value = Math.Abs(hash % 100000);
            return value.ToString("D5");
        }
    }

    private static UsersGroup EnsureTopGroup(UserReference userReference, string name, string description, ICollection<string> logs)
    {
        var existing = userReference.GetAllUsersGroup().OfType<UsersGroup>()
            .FirstOrDefault(x => IsSame(x.FullName.Value, name));
        if (existing != null)
        {
            logs.Add("Group exists: " + name);
            return existing;
        }

        var created = userReference.CreateReferenceObject(userReference.Classes.GroupBaseType) as UsersGroup;
        if (created == null)
        {
            throw new InvalidOperationException("Cannot create group: " + name);
        }

        created.FullName.Value = name;
        created.Description.Value = description;
        if (!created.EndChanges())
        {
            throw new InvalidOperationException("Cannot save group: " + name);
        }

        logs.Add("Group created: " + name);
        return created;
    }

    private static UsersGroup EnsureChildGroup(UserReference userReference, UsersGroup parent, string groupName, ICollection<string> logs)
    {
        var existing = userReference.GetAllUsersGroup().OfType<UsersGroup>().FirstOrDefault(x =>
            IsSame(x.FullName.Value, groupName) && x.Parent != null && x.Parent.Guid == parent.Guid);
        if (existing != null)
        {
            logs.Add("Study group exists: " + groupName);
            return existing;
        }

        var created = userReference.CreateReferenceObject(parent, userReference.Classes.GroupBaseType) as UsersGroup;
        if (created == null)
        {
            throw new InvalidOperationException("Cannot create study group: " + groupName);
        }

        created.FullName.Value = groupName;
        created.Description.Value = "Study group";
        if (!created.EndChanges())
        {
            throw new InvalidOperationException("Cannot save study group: " + groupName);
        }

        logs.Add("Study group created: " + groupName);
        return created;
    }

    private static User EnsureUser(UserReference userReference, StudentRow student, ICollection<string> logs)
    {
        var existing = FindUserByLogin(userReference, student.Login);
        if (existing != null)
        {
            logs.Add("User exists: " + student.Login);
            return existing;
        }

        var userClass = userReference.Classes.EmployerType ?? userReference.Classes.UserBaseType;
        var created = userReference.CreateReferenceObject(userClass) as User;
        if (created == null)
        {
            throw new InvalidOperationException("Cannot create user: " + student.Login);
        }

        created.Login.Value = student.Login;
        created.LastName.Value = student.LastName;
        created.FirstName.Value = student.FirstName;
        created.Patronymic.Value = student.MiddleName;
        created.ShortName.Value = (student.LastName ?? string.Empty).Trim() + " " + FirstLetter(student.FirstName) + "." + FirstLetter(student.MiddleName) + ".";
        created.FullName.Value = string.Join(" ", new[] { student.LastName, student.FirstName, student.MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        created.Password.Value = student.PinCode;

        if (!created.EndChanges())
        {
            throw new InvalidOperationException("Cannot save user: " + student.Login);
        }

        logs.Add("User created: " + student.Login);
        return created;
    }

    private static void EnsureMembership(UserReference userReference, UsersGroup group, User user, ICollection<string> logs)
    {
        if (IsMemberOfGroup(userReference, group, user.Login.Value))
        {
            return;
        }

        string error;
        if (!TryAddMembershipByGroupLink(group, user, out error) && !TryAddMembershipByUserLink(user, group, out error))
        {
            throw new InvalidOperationException("Cannot add user to group: " + user.Login.Value + " -> " + group.FullName.Value + ". " + error);
        }

        if (!IsMemberOfGroup(userReference, group, user.Login.Value))
        {
            throw new InvalidOperationException("Membership was not saved for user: " + user.Login.Value);
        }

        logs.Add("Membership ensured: " + user.Login.Value + " -> " + group.FullName.Value);
    }

    private static bool TryAddMembershipByGroupLink(UsersGroup group, User user, out string error)
    {
        try
        {
            group.BeginChanges(true);
            if (!group.CanCreateChildLink(user))
            {
                TryCancelChanges(group);
                error = "CanCreateChildLink=false";
                return false;
            }

            group.CreateChildLink(user);
            if (!group.EndChanges())
            {
                error = "Group.EndChanges=false";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            TryCancelChanges(group);
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static bool TryAddMembershipByUserLink(User user, UsersGroup group, out string error)
    {
        try
        {
            user.BeginChanges(true);
            if (!user.CanCreateParentLink(group))
            {
                TryCancelChanges(user);
                error = "CanCreateParentLink=false";
                return false;
            }

            user.CreateParentLink(group);
            if (!user.EndChanges())
            {
                error = "User.EndChanges=false";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            TryCancelChanges(user);
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static bool IsMemberOfGroup(UserReference userReference, UsersGroup group, string login)
    {
        var refreshed = userReference.GetAllUsersGroup().OfType<UsersGroup>()
            .FirstOrDefault(x => x.Guid == group.Guid) ?? group;

        return refreshed.GetAllInternalUsers(true).Any(x => IsSame(x.Login.Value, login));
    }

    private static User FindUserByLogin(UserReference userReference, string login)
    {
        var direct = userReference.FindUser(login);
        if (direct != null)
        {
            return direct;
        }

        return userReference.GetAllUsers()
            .OfType<User>()
            .FirstOrDefault(x => IsSame(x.Login.Value, login));
    }

    private static FolderObject EnsureFilesRootFolder(FileReference fileReference, ICollection<string> logs)
    {
        foreach (var candidate in FilesRootCandidates)
        {
            var folder = FindTopLevelFolderByName(fileReference, candidate) ?? TryFindFolderByPath(fileReference, candidate);
            if (folder == null)
            {
                continue;
            }

            logs.Add("Files root: " + NormalizePath(folder.Path.Value));
            return folder;
        }

        throw new InvalidOperationException("Root folder not found: " + FilesRootRu);
    }

    private static FolderObject EnsureChildFolder(FileReference fileReference, FolderObject parentFolder, string folderName, ICollection<string> logs)
    {
        var existing = FindDirectChildFolder(parentFolder, folderName);
        if (existing != null)
        {
            return existing;
        }

        var created = fileReference.CreatePath(folderName, parentFolder, CreateImportParameters());
        logs.Add("Folder created: " + NormalizePath(created.Path.Value));
        return created;
    }

    private static AssignmentState BuildAssignmentState(FileReference fileReference, FolderObject assignmentsRoot)
    {
        var state = new AssignmentState();
        if (assignmentsRoot == null)
        {
            return state;
        }

        var childFolders = GetChildFolders(assignmentsRoot)
            .Where(x => !string.IsNullOrWhiteSpace(x.Name.Value))
            .ToArray();

        var workFolders = childFolders
            .Where(x => WorkFolderRegex.IsMatch(x.Name.Value ?? string.Empty))
            .OrderBy(x => ParseOrder(x.Name.Value))
            .ToList();

        if (workFolders.Count == 0)
        {
            workFolders.AddRange(childFolders.Where(x => GetChildFiles(x).Count > 0));
        }

        foreach (var folder in workFolders)
        {
            var files = GetChildFiles(folder);
            if (files.Count > 0)
            {
                state.WorkFolders.Add(new WorkFolderState { Folder = folder, SourceFiles = files });
            }
        }

        if (state.WorkFolders.Count == 0)
        {
            var rootFiles = GetChildFiles(assignmentsRoot);
            if (rootFiles.Count > 0)
            {
                state.WorkFolders.Add(new WorkFolderState { Folder = assignmentsRoot, SourceFiles = rootFiles });
            }
        }

        return state;
    }

    private static void AssignTasksIfPossible(FileReference fileReference, AssignmentState state, FolderObject destinationFolder, ICollection<string> logs)
    {
        foreach (var work in state.WorkFolders)
        {
            if (work.SourceFiles.Count == 0)
            {
                continue;
            }

            var selected = SelectLeastUsed(work.SourceFiles, state.UsageByGuid);
            if (selected == null)
            {
                continue;
            }

            string assignedName;
            string error;
            if (TryCopyTaskFile(fileReference, selected, destinationFolder, work.Folder.Name.Value, out assignedName, out error))
            {
                IncreaseUsage(state.UsageByGuid, selected.Guid);
                logs.Add("Task assigned: " + assignedName);
            }
            else
            {
                logs.Add("Task assign failed: " + error);
            }
        }
    }

    private static FolderObject FindAssignmentsRoot(FileReference fileReference, FolderObject filesRootFolder)
    {
        var direct = FindDirectChildFolder(filesRootFolder, AssignmentsRu) ?? FindDirectChildFolder(filesRootFolder, AssignmentsTypoRu);
        if (direct != null)
        {
            return direct;
        }

        foreach (var candidate in AssignmentsRootCandidates)
        {
            var folder = TryFindFolderByPath(fileReference, candidate);
            if (folder != null)
            {
                return folder;
            }
        }

        return null;
    }

    private static FileObject SelectLeastUsed(IReadOnlyList<FileObject> sourceFiles, IDictionary<Guid, int> usageByGuid)
    {
        if (sourceFiles == null || sourceFiles.Count == 0)
        {
            return null;
        }

        var minUsage = sourceFiles.Min(x => GetUsage(usageByGuid, x.Guid));
        var candidates = sourceFiles.Where(x => GetUsage(usageByGuid, x.Guid) == minUsage).ToArray();
        return candidates.Length == 0 ? null : candidates[0];
    }

    private static bool TryCopyTaskFile(FileReference fileReference, FileObject sourceFile, FolderObject destinationFolder, string workFolderName, out string assignedName, out string error)
    {
        assignedName = string.Empty;
        error = string.Empty;

        if (sourceFile == null)
        {
            error = "Source file is null.";
            return false;
        }

        var sourceExt = Path.GetExtension(sourceFile.Name.Value);
        var targetName = NormalizeFileName(workFolderName) + (string.IsNullOrWhiteSpace(sourceExt) ? ".bin" : sourceExt);

        if (TryCopy(fileReference, sourceFile, destinationFolder, targetName, out error))
        {
            assignedName = targetName;
            return true;
        }

        return false;
    }

    private static bool TryCopy(FileReference fileReference, FileObject sourceFile, FolderObject destinationFolder, string targetName, out string error)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tflex_macro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, targetName);

        try
        {
            sourceFile.Export(tempFile, true);
            fileReference.AddFile(tempFile, destinationFolder);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private static FolderObject FindTopLevelFolderByName(FileReference fileReference, string folderName)
    {
        try
        {
            return fileReference.Objects
                .OfType<FolderObject>()
                .FirstOrDefault(x => IsSame(x.Name.Value, folderName));
        }
        catch
        {
            return null;
        }
    }

    private static FolderObject FindDirectChildFolder(FolderObject parentFolder, string folderName)
    {
        if (parentFolder == null || string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        return GetChildFolders(parentFolder).FirstOrDefault(x => IsSame(x.Name.Value, folderName));
    }

    private static FolderObject TryFindFolderByPath(FileReference fileReference, string path)
    {
        return TryFindByPath(fileReference, path) as FolderObject;
    }

    private static FileReferenceObject TryFindByPath(FileReference fileReference, string path)
    {
        foreach (var candidate in BuildPathCandidates(path))
        {
            var absolute = TryFindByPathCore(fileReference.FindByPath, candidate);
            if (absolute != null)
            {
                return absolute;
            }
        }

        foreach (var candidate in BuildPathCandidates(path))
        {
            var relative = TryFindByPathCore(fileReference.FindByRelativePath, candidate);
            if (relative != null)
            {
                return relative;
            }
        }

        return null;
    }

    private static FileReferenceObject TryFindByPathCore(Func<string, FileReferenceObject> finder, string path)
    {
        try
        {
            return finder(path);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> BuildPathCandidates(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized.Length == 0)
        {
            yield break;
        }

        yield return normalized;
        yield return normalized.Replace('/', '\\');
    }

    private static List<FolderObject> GetChildFolders(FolderObject folder)
    {
        folder.Load(true);
        return folder.Children.OfType<FolderObject>().ToList();
    }

    private static List<FileObject> GetChildFiles(FolderObject folder)
    {
        folder.Load(true);
        return folder.Children.OfType<FileObject>().ToList();
    }

    private static ImportParameters CreateImportParameters()
    {
        return new ImportParameters
        {
            Recursive = true,
            CreateClasses = true,
            AutoCheckIn = true,
            UpdateExistingFiles = false
        };
    }

    private static void TryCancelChanges(TFlex.DOCs.Model.References.ReferenceObject referenceObject)
    {
        if (referenceObject == null)
        {
            return;
        }

        try
        {
            referenceObject.CancelChanges();
        }
        catch
        {
            // Ignore cancel failures.
        }
    }

    private static int ParseOrder(string name)
    {
        var match = Regex.Match(name ?? string.Empty, "\\d+");
        int value;
        return match.Success && int.TryParse(match.Value, out value) ? value : int.MaxValue;
    }

    private static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static bool IsSame(string left, string right)
    {
        return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFileName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            value = "Work";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    private static int GetUsage(IDictionary<Guid, int> usageByGuid, Guid guid)
    {
        int value;
        return usageByGuid.TryGetValue(guid, out value) ? value : 0;
    }

    private static void IncreaseUsage(IDictionary<Guid, int> usageByGuid, Guid guid)
    {
        usageByGuid[guid] = GetUsage(usageByGuid, guid) + 1;
    }

    private sealed class AssignmentState
    {
        public List<WorkFolderState> WorkFolders { get; } = new List<WorkFolderState>();
        public Dictionary<Guid, int> UsageByGuid { get; } = new Dictionary<Guid, int>();
    }

    private sealed class WorkFolderState
    {
        public FolderObject Folder { get; set; }
        public List<FileObject> SourceFiles { get; set; }
    }

    private sealed class StudentRow
    {
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string Login { get; set; }
        public string PinCode { get; set; }
        public string FolderName { get; set; }
    }
}
