using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using TFlex.DOCs.Common;
using TFlex.DOCs.Common.Encryption;
using TFlex.DOCs.Model;
using TFlex.DOCs.Model.Access;
using TFlex.DOCs.Model.Configuration;
using TFlex.DOCs.Model.Macros;
using TFlex.DOCs.Model.References.Files;
using TFlex.DOCs.Model.References.Macros;
using TFlex.DOCs.Model.References.Users;
using TFlex.DOCs.Model.Structure;
using TFlex.PdmFramework.Resolve;

namespace TFlexDocsAdapter;

internal static class Program
{
    private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings
    {
        UseSimpleDictionaryFormat = true
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: TFlexDocsAdapter.exe check-connection|execute-foundation");
            return 2;
        }

        AssemblyBootstrap.Initialize(AppDomain.CurrentDomain.BaseDirectory);

        try
        {
            var command = args[0].Trim();
            if (string.Equals(command, "check-connection", StringComparison.OrdinalIgnoreCase))
            {
                var request = ReadJson<TFlexConnectionRequest>();
                if (request == null)
                {
                    WriteJson(TFlexConnectionCheckResult.Fail("Invalid JSON payload."));
                    return 1;
                }

                var result = TFlexConnector.CheckConnection(request);
                WriteJson(result);
                return result.Success ? 0 : 1;
            }

            if (string.Equals(command, "execute-foundation", StringComparison.OrdinalIgnoreCase))
            {
                var request = ReadJson<TFlexProvisioningExecuteRequest>();
                if (request == null)
                {
                    WriteJson(TFlexProvisioningExecuteResult.Fail("Invalid JSON payload."));
                    return 1;
                }

                var result = TFlexProvisioner.ExecuteFoundation(request);
                WriteJson(result);
                return result.Success ? 0 : 1;
            }

            Console.Error.WriteLine("Unsupported command: " + command);
            return 2;
        }
        catch (Exception ex)
        {
            WriteJson(TFlexConnectionCheckResult.Fail("Adapter fatal error: " + ex.GetType().Name + ": " + ex.Message));
            return 1;
        }
    }

    private static T ReadJson<T>() where T : class
    {
        var serializer = new DataContractJsonSerializer(typeof(T), JsonSettings);
        return serializer.ReadObject(Console.OpenStandardInput()) as T;
    }

    private static void WriteJson(object payload)
    {
        var serializer = new DataContractJsonSerializer(payload.GetType(), JsonSettings);
        serializer.WriteObject(Console.OpenStandardOutput(), payload);
    }
}

internal static class TFlexConnector
{
    public static TFlexConnectionCheckResult CheckConnection(TFlexConnectionRequest options)
    {
        var diagnostics = ConnectionFactory.ValidateOptions(options);
        diagnostics.AddRange(AssemblyBootstrap.ValidateLocalAssemblySet());

        if (diagnostics.Any(x => x.StartsWith("Server is required", StringComparison.OrdinalIgnoreCase) ||
                                 x.StartsWith("UserName is required", StringComparison.OrdinalIgnoreCase) ||
                                 x.StartsWith("Password is required", StringComparison.OrdinalIgnoreCase) ||
                                 x.StartsWith("AccessToken is required", StringComparison.OrdinalIgnoreCase)))
        {
            return TFlexConnectionCheckResult.Fail("TFlex configuration is incomplete.", diagnostics.ToArray());
        }

        try
        {
            using (var connection = ConnectionFactory.OpenConnection(options, diagnostics))
            {
                var result = new TFlexConnectionCheckResult
                {
                    Success = true,
                    Message = "Connected to T-FLEX DOCs.",
                    ServerVersion = connection.Version != null ? connection.Version.ToString() : null,
                    IsAdministrator = connection.IsAdministrator,
                    MissingDependencies = diagnostics.ToArray()
                };

                connection.Close();
                return result;
            }
        }
        catch (Exception ex)
        {
            ConnectionFactory.AppendExceptionChain(diagnostics, ex);
            return TFlexConnectionCheckResult.Fail("Connection error: " + ex.GetType().Name + ": " + ex.Message, diagnostics.ToArray());
        }
    }
}

internal static class TFlexProvisioner
{
    private const string StudentsGroupName = "\u0421\u0442\u0443\u0434\u0435\u043d\u0442\u044b"; // Студенты
    private const string TeachersGroupName = "\u041f\u0440\u0435\u043f\u043e\u0434\u0430\u0432\u0430\u0442\u0435\u043b\u0438"; // Преподаватели
    private const string FilesRootFolder = "\u0424\u0430\u0439\u043b\u044b"; // Файлы
    private const string StudentsRootFolderName = StudentsGroupName;
    private const string AssignmentsRootFolderName = "\u0417\u0430\u0434\u0430\u043d\u0438\u044f"; // Задания
    private const string AssignmentsTypoRootFolderName = "\u0417\u0434\u0430\u043d\u0438\u044f"; // Здания
    private const string ServiceFolderName = "\u0421\u043b\u0443\u0436\u0435\u0431\u043d\u0430\u044f"; // Служебная
    private const string WorkFolderBaseName = "\u0420\u0430\u0431\u043e\u0442\u0430"; // Работа

    private static readonly string[] FilesRootCandidates = new[]
    {
        FilesRootFolder,
        "Files"
    };

    private static readonly string[] StudentsRootCandidates = new[]
    {
        FilesRootFolder + "/" + StudentsRootFolderName,
        StudentsRootFolderName,
        "Students"
    };

    private static readonly string[] AssignmentsRootCandidates = new[]
    {
        FilesRootFolder + "/" + AssignmentsRootFolderName,
        AssignmentsRootFolderName,
        FilesRootFolder + "/" + AssignmentsTypoRootFolderName,
        AssignmentsTypoRootFolderName,
        ServiceFolderName + "/" + AssignmentsRootFolderName,
        "Assignments"
    };

    private static readonly Regex WorkFolderRegex =
        new Regex("^\\s*" + WorkFolderBaseName + "\\s*\\d+\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> CadExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".grb", ".grs", ".grd", ".grx", ".2d", ".3d", ".tfl", ".tflex"
    };
    private static readonly Random Random = new Random();

    public static TFlexProvisioningExecuteResult ExecuteFoundation(TFlexProvisioningExecuteRequest request)
    {
        var diagnostics = new List<string>();
        var logs = new List<string>();
        var warnings = new List<string>();
        var executedActions = 0;

        if (request == null)
        {
            return TFlexProvisioningExecuteResult.Fail("Provisioning request is null.");
        }

        var options = request.Connection ?? new TFlexConnectionRequest();
        diagnostics.AddRange(ConnectionFactory.ValidateOptions(options));
        diagnostics.AddRange(AssemblyBootstrap.ValidateLocalAssemblySet());

        if (string.IsNullOrWhiteSpace(request.GroupName))
        {
            return BuildFail("GroupName is empty.");
        }

        if (request.Students == null || request.Students.Count == 0)
        {
            return BuildFail("Students list is empty.");
        }

        if (diagnostics.Any(x => x.StartsWith("Server is required", StringComparison.OrdinalIgnoreCase) ||
                                 x.StartsWith("UserName is required", StringComparison.OrdinalIgnoreCase) ||
                                 x.StartsWith("Password is required", StringComparison.OrdinalIgnoreCase) ||
                                 x.StartsWith("AccessToken is required", StringComparison.OrdinalIgnoreCase)))
        {
            return BuildFail("TFlex configuration is incomplete.");
        }

        try
        {
            using (var connection = ConnectionFactory.OpenConnection(options, diagnostics))
            {
                var userReference = new UserReference(connection);
                var fileReference = new FileReference(connection);
                var assignTasks = request.AssignTasks != false;
                var folderMacroName = (options.FolderCreationMacroName ?? string.Empty).Trim();
                logs.Add("РџРѕРґРєР»СЋС‡РµРЅРѕ Рє СЃРµСЂРІРµСЂСѓ '" + (connection.ServerName ?? "<unknown>") + "'.");
                logs.Add("РђРєС‚РёРІРЅР°СЏ РєРѕРЅС„РёРіСѓСЂР°С†РёСЏ: " + DescribeConfiguration(connection.CurrentConfiguration) + ".");
                if (folderMacroName.Length > 0)
                {
                    logs.Add("Folder creation macro mode enabled. Macro='" + folderMacroName + "'.");
                }

                var studentsRootGroup = EnsureTopGroup(userReference, StudentsGroupName, "РЎС‚СѓРґРµРЅС‡РµСЃРєРёРµ РіСЂСѓРїРїС‹", logs, ref executedActions);
                var targetGroup = EnsureChildGroup(userReference, studentsRootGroup, request.GroupName, logs, ref executedActions);
                var teachersGroup = EnsureTopGroup(userReference, TeachersGroupName, "РџСЂРµРїРѕРґР°РІР°С‚РµР»Рё", logs, ref executedActions);

                var filesRootFolder = EnsureFilesRootFolder(fileReference, logs);
                var studentsRootFolder = EnsureStudentsRootFolder(fileReference, filesRootFolder, folderMacroName, logs, warnings, ref executedActions);
                var groupFolder = EnsureChildFolder(fileReference, studentsRootFolder, request.GroupName, folderMacroName, logs, warnings, ref executedActions);
                var groupFolderPath = NormalizePath(groupFolder.Path.Value);

                ApplyAcl(groupFolder,
                    "РїР°РїРєР° РіСЂСѓРїРїС‹ '" + groupFolderPath + "'",
                    new[] { (UserReferenceObject)targetGroup, teachersGroup },
                    Array.Empty<UserReferenceObject>(),
                    logs, warnings, ref executedActions);

                FolderObject assignmentsRoot = null;
                var assignmentState = new AssignmentState();
                if (assignTasks)
                {
                    try
                    {
                        assignmentsRoot = FindAssignmentsRoot(fileReference, filesRootFolder, logs);
                        if (assignmentsRoot == null)
                        {
                            try
                            {
                                assignmentsRoot = EnsureAssignmentsRootFolder(fileReference, filesRootFolder, folderMacroName, logs, warnings, ref executedActions);
                                logs.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' РѕС‚СЃСѓС‚СЃС‚РІРѕРІР°Р»Р° Рё Р±С‹Р»Р° СЃРѕР·РґР°РЅР° Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё.");
                            }
                            catch (Exception ex)
                            {
                                warnings.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' РЅРµ РЅР°Р№РґРµРЅР° Рё РЅРµ РјРѕР¶РµС‚ Р±С‹С‚СЊ СЃРѕР·РґР°РЅР°: " + ex.GetType().Name + ": " + ex.Message);
                            }
                        }

                        if (assignmentsRoot != null)
                        {
                            ApplyAcl(assignmentsRoot,
                                "РєРѕСЂРЅРµРІР°СЏ РїР°РїРєР° 'Р—Р°РґР°РЅРёСЏ'",
                                new[] { (UserReferenceObject)teachersGroup },
                                new[] { (UserReferenceObject)studentsRootGroup },
                                logs, warnings, ref executedActions);
                        }
                        else
                        {
                            warnings.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' РЅРµ РЅР°Р№РґРµРЅР°, ACL-Р·Р°РїСЂРµС‚ СЃС‚СѓРґРµРЅС‚Р°Рј РЅРµ РїСЂРёРјРµРЅРµРЅ.");
                        }

                        assignmentState = BuildAssignmentState(fileReference, assignmentsRoot, warnings);
                    }
                    catch (Exception ex)
                    {
                        assignTasks = false;
                        warnings.Add("РќРµ СѓРґР°Р»РѕСЃСЊ РїРѕРґРіРѕС‚РѕРІРёС‚СЊ СЂР°Р·РґР°С‡Сѓ Р·Р°РґР°РЅРёР№: " + ex.GetType().Name + ": " + ex.Message);
                        warnings.Add("Р Р°Р·РґР°С‡Р° Р·Р°РґР°РЅРёР№ РѕС‚РєР»СЋС‡РµРЅР° РґР»СЏ С‚РµРєСѓС‰РµРіРѕ Р·Р°РїСѓСЃРєР°. РЎРѕР·РґР°РЅРёРµ РїРѕР»СЊР·РѕРІР°С‚РµР»РµР№ Рё РїР°РїРѕРє РїСЂРѕРґРѕР»Р¶РµРЅРѕ.");
                    }
                }
                else
                {
                    logs.Add("Р Р°Р·РґР°С‡Р° Р·Р°РґР°РЅРёР№ РѕС‚РєР»СЋС‡РµРЅР° (assignTasks=false). РЎРѕР·РґР°РЅС‹ С‚РѕР»СЊРєРѕ РїРѕР»СЊР·РѕРІР°С‚РµР»Рё Рё РїР°РїРєРё.");
                }

                var usersWithUnconfirmedMembership = new List<UserReferenceObject>();
                foreach (var student in request.Students)
                {
                    var user = EnsureUser(userReference, targetGroup, student, logs, ref executedActions);
                    var membershipConfirmed = EnsureMembership(userReference, targetGroup, user, logs, ref executedActions);
                    if (!membershipConfirmed)
                    {
                        usersWithUnconfirmedMembership.Add(user);
                        warnings.Add("Membership fallback: user '" + user.Login.Value + "' is linked to group '" + targetGroup.FullName.Value + "', but API has not confirmed membership yet. Adding explicit ACL on group folder.");
                    }

                    var studentFolder = EnsureChildFolder(fileReference, groupFolder, student.FolderName, folderMacroName, logs, warnings, ref executedActions);
                    var studentFolderPath = NormalizePath(studentFolder.Path.Value);
                    var tasksFolder = EnsureChildFolder(fileReference, studentFolder, AssignmentsRootFolderName, folderMacroName, logs, warnings, ref executedActions);

                    ApplyAcl(studentFolder,
                        "РїР°РїРєР° СЃС‚СѓРґРµРЅС‚Р° '" + studentFolderPath + "'",
                        new[] { (UserReferenceObject)user, teachersGroup },
                        Array.Empty<UserReferenceObject>(),
                        logs, warnings, ref executedActions);

                    ApplyAcl(tasksFolder,
                        "РїР°РїРєР° Р·Р°РґР°РЅРёР№ СЃС‚СѓРґРµРЅС‚Р° '" + studentFolderPath + "/" + AssignmentsRootFolderName + "'",
                        new[] { (UserReferenceObject)user, teachersGroup },
                        Array.Empty<UserReferenceObject>(),
                        logs, warnings, ref executedActions);

                    if (assignTasks)
                    {
                        AssignTasks(fileReference, tasksFolder, assignmentState, student, logs, warnings, ref executedActions);
                    }
                }

                if (usersWithUnconfirmedMembership.Count > 0)
                {
                    var editors = new List<UserReferenceObject>
                    {
                        targetGroup,
                        teachersGroup
                    };
                    editors.AddRange(usersWithUnconfirmedMembership);
                    ApplyAcl(groupFolder,
                        "group folder fallback ACL '" + groupFolderPath + "'",
                        editors,
                        Array.Empty<UserReferenceObject>(),
                        logs, warnings, ref executedActions);
                }

                VerifyProvisioningResult(
                    userReference,
                    fileReference,
                    targetGroup,
                    groupFolder,
                    request.Students,
                    logs,
                    warnings);

                connection.Close();
            }

            return new TFlexProvisioningExecuteResult
            {
                Success = true,
                Message = "Foundation provisioning completed.",
                PlannedActions = request.PlannedActions,
                ExecutedActions = executedActions,
                Logs = logs.ToArray(),
                Warnings = warnings.ToArray(),
                MissingDependencies = diagnostics.ToArray()
            };
        }
        catch (Exception ex)
        {
            ConnectionFactory.AppendExceptionChain(diagnostics, ex);
            return new TFlexProvisioningExecuteResult
            {
                Success = false,
                Message = "Provisioning error: " + ex.GetType().Name + ": " + ex.Message,
                PlannedActions = request.PlannedActions,
                ExecutedActions = executedActions,
                Logs = logs.ToArray(),
                Warnings = warnings.ToArray(),
                MissingDependencies = diagnostics.ToArray()
            };
        }

        TFlexProvisioningExecuteResult BuildFail(string message)
        {
            return new TFlexProvisioningExecuteResult
            {
                Success = false,
                Message = message,
                PlannedActions = request != null ? request.PlannedActions : 0,
                ExecutedActions = 0,
                Logs = logs.ToArray(),
                Warnings = warnings.ToArray(),
                MissingDependencies = diagnostics.ToArray()
            };
        }
    }

    private static UsersGroup EnsureTopGroup(UserReference userReference, string name, string description, ICollection<string> logs, ref int executed)
    {
        var existing = userReference.GetAllUsersGroup().OfType<UsersGroup>()
            .FirstOrDefault(x => IsSame(x.FullName.Value, name));
        if (existing != null)
        {
            logs.Add("Р“СЂСѓРїРїР° '" + name + "' СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚.");
            return existing;
        }

        var created = userReference.CreateReferenceObject(userReference.Classes.GroupBaseType) as UsersGroup;
        if (created == null)
        {
            throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕР·РґР°С‚СЊ РіСЂСѓРїРїСѓ '" + name + "'.");
        }

        created.FullName.Value = name;
        created.Description.Value = description;
        if (!created.EndChanges())
        {
            throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕС…СЂР°РЅРёС‚СЊ РіСЂСѓРїРїСѓ '" + name + "'.");
        }

        executed++;
        logs.Add("РЎРѕР·РґР°РЅР° РіСЂСѓРїРїР° '" + name + "'.");
        return created;
    }

    private static UsersGroup EnsureChildGroup(UserReference userReference, UsersGroup parent, string groupName, ICollection<string> logs, ref int executed)
    {
        var existing = userReference.GetAllUsersGroup().OfType<UsersGroup>().FirstOrDefault(x =>
            IsSame(x.FullName.Value, groupName) && x.Parent != null && x.Parent.Guid == parent.Guid);
        if (existing != null)
        {
            logs.Add("Р“СЂСѓРїРїР° '" + groupName + "' СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚.");
            return existing;
        }

        var created = userReference.CreateReferenceObject(parent, userReference.Classes.GroupBaseType) as UsersGroup;
        if (created == null)
        {
            throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕР·РґР°С‚СЊ РіСЂСѓРїРїСѓ '" + groupName + "'.");
        }

        created.FullName.Value = groupName;
        created.Description.Value = "РЈС‡РµР±РЅР°СЏ РіСЂСѓРїРїР°";
        if (!created.EndChanges())
        {
            throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕС…СЂР°РЅРёС‚СЊ РіСЂСѓРїРїСѓ '" + groupName + "'.");
        }

        executed++;
        logs.Add("РЎРѕР·РґР°РЅР° РіСЂСѓРїРїР° '" + groupName + "'.");
        return created;
    }

    private static User EnsureUser(UserReference userReference, UsersGroup targetGroup, TFlexProvisioningStudent student, ICollection<string> logs, ref int executed)
    {
        var existing = FindUserByLogin(userReference, student.Login);
        if (existing != null)
        {
            logs.Add("РџРѕР»СЊР·РѕРІР°С‚РµР»СЊ '" + student.Login + "' СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚.");
            return existing;
        }

        var userClass = userReference.Classes.EmployerType ?? userReference.Classes.UserBaseType;
        var created = targetGroup != null
            ? userReference.CreateReferenceObject(targetGroup, userClass) as User
            : userReference.CreateReferenceObject(userClass) as User;
        if (created == null)
        {
            throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕР·РґР°С‚СЊ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ '" + student.Login + "'.");
        }

        created.Login.Value = student.Login;
        created.LastName.Value = student.LastName;
        created.FirstName.Value = student.FirstName;
        created.Patronymic.Value = student.MiddleName;
        created.ShortName.Value = (student.LastName ?? string.Empty).Trim() + " " + FirstLetter(student.FirstName) + "." + FirstLetter(student.MiddleName) + ".";
        created.FullName.Value = string.Join(" ", new[] { student.LastName, student.FirstName, student.MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

        if (!string.IsNullOrWhiteSpace(student.PinCode))
        {
            created.Password.Value = student.PinCode;
        }

        try
        {
            if (!created.EndChanges())
            {
                throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕС…СЂР°РЅРёС‚СЊ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ '" + student.Login + "'.");
            }
        }
        catch (Exception ex) when (IsDuplicateUserLoginError(ex))
        {
            var resolved = FindUserByLogin(userReference, student.Login);
            if (resolved != null)
            {
                logs.Add("РџРѕР»СЊР·РѕРІР°С‚РµР»СЊ '" + student.Login + "' СѓР¶Рµ СЃСѓС‰РµСЃС‚РІРѕРІР°Р», РёСЃРїРѕР»СЊР·РѕРІР°РЅ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёР№ РѕР±СЉРµРєС‚.");
                return resolved;
            }

            throw;
        }

        executed++;
        if (targetGroup != null)
        {
            logs.Add("РЎРѕР·РґР°РЅ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ '" + student.Login + "' РІ РіСЂСѓРїРїРµ '" + targetGroup.FullName.Value + "'.");
        }
        else
        {
            logs.Add("РЎРѕР·РґР°РЅ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ '" + student.Login + "'.");
        }
        return created;
    }

    private static bool EnsureMembership(UserReference userReference, UsersGroup group, User user, ICollection<string> logs, ref int executed)
    {
        if (userReference == null) throw new ArgumentNullException(nameof(userReference));
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (user == null) throw new ArgumentNullException(nameof(user));

        var login = user.Login.Value;
        if (IsMemberOfGroup(userReference, group, login))
        {
            return true;
        }

        var attempts = new List<string>();
        var linkOperationSucceeded = false;

        string firstError;
        if (!TryAddMembershipByGroupLink(group, user, out firstError))
        {
            attempts.Add("group-link: " + firstError);
        }
        else
        {
            linkOperationSucceeded = true;
        }

        if (IsMemberOfGroup(userReference, group, login))
        {
            executed++;
            logs.Add("Пользователь '" + login + "' добавлен в группу '" + group.FullName.Value + "' (group-link).");
            return true;
        }

        attempts.Add("group-link: membership still missing after EndChanges().");

        string secondError;
        if (!TryAddMembershipByUserLink(user, group, out secondError))
        {
            attempts.Add("user-link: " + secondError);
        }
        else
        {
            linkOperationSucceeded = true;
        }

        if (IsMemberOfGroup(userReference, group, login))
        {
            executed++;
            logs.Add("Пользователь '" + login + "' добавлен в группу '" + group.FullName.Value + "' (user-link).");
            return true;
        }

        attempts.Add("user-link: membership still missing after EndChanges().");

        string apiLinkError;
        if (!TryAddMembershipByApiLinkGroup(group, user, out apiLinkError))
        {
            attempts.Add("api-link: " + apiLinkError);
        }
        else
        {
            linkOperationSucceeded = true;
        }

        if (IsMemberOfGroup(userReference, group, login))
        {
            executed++;
            logs.Add("Пользователь '" + login + "' добавлен в группу '" + group.FullName.Value + "' (api-link).");
            return true;
        }

        attempts.Add("api-link: membership still missing after EndChanges().");

        if (WaitForMembership(userReference, group, login, retries: 6, delayMs: 350))
        {
            executed++;
            logs.Add("Пользователь '" + login + "' появился в группе '" + group.FullName.Value + "' после повторной проверки.");
            return true;
        }

        if (linkOperationSucceeded)
        {
            executed++;
            logs.Add("Связь пользователя '" + login + "' с группой '" + group.FullName.Value + "' создана, но API не подтвердил членство. Продолжаем с fallback ACL.");
            return false;
        }

        throw new InvalidOperationException(
            "Пользователь '" + login + "' не появился в группе '" + group.FullName.Value + "' после всех попыток. " +
            string.Join(" | ", attempts));
    }

    private static FolderObject EnsureFilesRootFolder(FileReference fileReference, ICollection<string> logs)
    {
        foreach (var candidate in FilesRootCandidates)
        {
            var folder = FindTopLevelFolderByName(fileReference, candidate) ?? TryFindFolderByPath(fileReference, candidate);
            if (folder == null) continue;

            if (!string.Equals(candidate, FilesRootFolder, StringComparison.OrdinalIgnoreCase))
            {
                logs.Add("РљРѕСЂРЅРµРІР°СЏ РїР°РїРєР° 'Р¤Р°Р№Р»С‹' РЅР°Р№РґРµРЅР° РїРѕ Р°Р»СЊС‚РµСЂРЅР°С‚РёРІРЅРѕРјСѓ РёРјРµРЅРё '" + candidate + "'.");
            }

            logs.Add("Р’С‹Р±СЂР°РЅ РєРѕСЂРµРЅСЊ СЃРїСЂР°РІРѕС‡РЅРёРєР° С„Р°Р№Р»РѕРІ: '" + NormalizePath(folder.Path.Value) + "'.");
            return folder;
        }

        foreach (var candidate in StudentsRootCandidates.Concat(AssignmentsRootCandidates))
        {
            var folder = TryFindFolderByPath(fileReference, candidate);
            var resolvedRoot = TryResolveTopLevelFolder(fileReference, folder);
            if (resolvedRoot == null)
            {
                continue;
            }

            logs.Add("Root folder inferred from existing path '" + NormalizePath(folder.Path.Value) + "'.");
            logs.Add("Selected files root: '" + NormalizePath(resolvedRoot.Path.Value) + "'.");
            return resolvedRoot;
        }

        var accessibleRoot = FindAccessibleTopLevelFolder(fileReference);
        if (accessibleRoot != null)
        {
            logs.Add("Root folder auto-selected from accessible top-level folders: '" + NormalizePath(accessibleRoot.Path.Value) + "'.");
            return accessibleRoot;
        }

        throw new InvalidOperationException("Root files folder not found. Checked candidates: '" + string.Join("', '", FilesRootCandidates) + "'.");
    }

    private static FolderObject EnsureChildFolder(
        FileReference fileReference,
        FolderObject parentFolder,
        string folderName,
        string folderMacroName,
        ICollection<string> logs,
        ICollection<string> warnings,
        ref int executed)
    {
        if (parentFolder == null) throw new ArgumentNullException(nameof(parentFolder));
        var targetName = (folderName ?? string.Empty).Trim();
        if (targetName.Length == 0) throw new ArgumentException("Folder name is empty.", nameof(folderName));

        var existing = FindDirectChildFolder(parentFolder, targetName);
        if (existing != null)
        {
            logs.Add("РџР°РїРєР° СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚: '" + NormalizePath(existing.Path.Value) + "'.");
            return existing;
        }

        var targetPath = NormalizePath(parentFolder.Path.Value + "/" + targetName);
        if (TryEnsureFolderViaMacro(parentFolder.Reference.Connection, fileReference, folderMacroName, targetPath, out var macroFolder, out var macroInfo))
        {
            if (!string.IsNullOrWhiteSpace(macroInfo))
            {
                logs.Add(macroInfo);
            }

            if (macroFolder != null)
            {
                executed++;
                logs.Add("РЎРѕР·РґР°РЅР° РїР°РїРєР° '" + NormalizePath(macroFolder.Path.Value) + "' (via macro).");
                return macroFolder;
            }
        }
        else if (!string.IsNullOrWhiteSpace(macroInfo))
        {
            warnings.Add(macroInfo);
        }

        var created = fileReference.CreatePath(targetName, parentFolder, CreateImportParameters());
        executed++;
        logs.Add("РЎРѕР·РґР°РЅР° РїР°РїРєР° '" + NormalizePath(created.Path.Value) + "'.");
        return created;
    }

    private static FolderObject EnsureStudentsRootFolder(
        FileReference fileReference,
        FolderObject filesRootFolder,
        string folderMacroName,
        ICollection<string> logs,
        ICollection<string> warnings,
        ref int executed)
    {
        var directChild = FindDirectChildFolder(filesRootFolder, StudentsRootFolderName);
        if (directChild != null)
        {
            return directChild;
        }

        foreach (var candidate in StudentsRootCandidates)
        {
            var folder = TryFindFolderByPath(fileReference, candidate);
            if (folder != null)
            {
                if (!IsInsideFolder(folder, filesRootFolder))
                {
                    logs.Add("РќР°Р№РґРµРЅР° РїР°РїРєР° 'РЎС‚СѓРґРµРЅС‚С‹' РІРЅРµ РєРѕСЂРЅСЏ 'Р¤Р°Р№Р»С‹' ('" + NormalizePath(folder.Path.Value) + "'). Р‘СѓРґРµС‚ СЃРѕР·РґР°РЅР° РїСЂР°РІРёР»СЊРЅР°СЏ РІРµС‚РєР° РІ '" + NormalizePath(filesRootFolder.Path.Value) + "'.");
                    continue;
                }

                if (!string.Equals(candidate, FilesRootFolder + "/" + StudentsRootFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    logs.Add("РџР°РїРєР° 'РЎС‚СѓРґРµРЅС‚С‹' РЅР°Р№РґРµРЅР° РїРѕ Р°Р»СЊС‚РµСЂРЅР°С‚РёРІРЅРѕРјСѓ РїСѓС‚Рё '" + candidate + "'.");
                }

                return folder;
            }
        }

        var created = EnsureChildFolder(fileReference, filesRootFolder, StudentsRootFolderName, folderMacroName, logs, warnings, ref executed);
        logs.Add("РџР°РїРєР° 'РЎС‚СѓРґРµРЅС‚С‹' СЃРѕР·РґР°РЅР° РїРѕ РїСѓС‚Рё '" + NormalizePath(created.Path.Value) + "'.");
        return created;
    }

    private static FolderObject EnsureAssignmentsRootFolder(
        FileReference fileReference,
        FolderObject filesRootFolder,
        string folderMacroName,
        ICollection<string> logs,
        ICollection<string> warnings,
        ref int executed)
    {
        var created = EnsureChildFolder(fileReference, filesRootFolder, AssignmentsRootFolderName, folderMacroName, logs, warnings, ref executed);
        logs.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' СЃРѕР·РґР°РЅР° РїРѕ РїСѓС‚Рё '" + NormalizePath(created.Path.Value) + "'.");
        return created;
    }

    private static bool TryEnsureFolderViaMacro(
        ServerConnection connection,
        FileReference fileReference,
        string folderMacroName,
        string targetPath,
        out FolderObject folder,
        out string info)
    {
        folder = null;
        info = string.Empty;

        var macroName = (folderMacroName ?? string.Empty).Trim();
        if (macroName.Length == 0)
        {
            return false;
        }

        try
        {
            var macroReference = connection.References.Macros;
            if (macroReference == null)
            {
                info = "Folder macro mode is enabled, but connection.References.Macros is null. Falling back to API folder creation.";
                return false;
            }

            Macro macro = null;
            Guid macroGuid;
            if (Guid.TryParse(macroName, out macroGuid))
            {
                macro = macroReference.Find(macroGuid);
            }

            if (macro == null)
            {
                macro = macroReference.Find(macroName);
            }

            if (macro == null)
            {
                info = "Folder macro '" + macroName + "' not found. Falling back to API folder creation.";
                return false;
            }

            var context = new MacroContext(connection);
            object result = macro.Run(context, "EnsureFolderPath", new object[] { targetPath });
            var resolvedPath = NormalizePath(result as string);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                resolvedPath = NormalizePath(targetPath);
            }

            folder = TryFindFolderByPath(fileReference, resolvedPath) ?? TryFindFolderByPath(fileReference, NormalizePath(targetPath));
            if (folder == null)
            {
                info = "Macro '" + (macro.Name != null ? macro.Name.Value : macroName) + "' executed, but folder '" + NormalizePath(targetPath) + "' was not found. Falling back to API folder creation.";
                return false;
            }

            info = "Folder macro '" + (macro.Name != null ? macro.Name.Value : macroName) + "' ensured path '" + NormalizePath(folder.Path.Value) + "'.";
            return true;
        }
        catch (Exception ex)
        {
            info = "Folder macro '" + macroName + "' failed: " + ex.GetType().Name + ": " + ex.Message + ". Falling back to API folder creation.";
            return false;
        }
    }

    private static void ApplyAcl(
        FolderObject folder,
        string scope,
        IEnumerable<UserReferenceObject> allowEditors,
        IEnumerable<UserReferenceObject> denyOwners,
        ICollection<string> logs,
        ICollection<string> warnings,
        ref int executed)
    {
        try
        {
            var editors = UniqueOwners(allowEditors);
            var denied = UniqueOwners(denyOwners);
            if (editors.Count == 0 && denied.Count == 0)
            {
                return;
            }

            var manager = AccessManager.GetReferenceObjectAccess(folder, AccessRightsLoadOptions.EditorMode);
            if (manager == null)
            {
                warnings.Add("ACL РЅРµ РїСЂРёРјРµРЅРµРЅ (" + scope + "): AccessManager РІРµСЂРЅСѓР» null.");
                return;
            }

            // Convert inherited ACL to explicit ACL before setting per-user rules.
            if (manager.IsInherit)
            {
                manager.SetInherit(inherit: false, copyInheritAccess: true);
            }

            var editorGroup = editors.Count > 0 ? SelectAccessGroup(folder.Reference.Connection, deny: false) : null;
            var denyGroup = denied.Count > 0 ? SelectAccessGroup(folder.Reference.Connection, deny: true) : null;

            foreach (var owner in editors)
            {
                if (editorGroup == null) break;
                TryRemove(manager, owner, editorGroup.Type);
                manager.SetAccess(owner, editorGroup);
            }

            foreach (var owner in denied)
            {
                if (denyGroup == null) break;
                TryRemove(manager, owner, denyGroup.Type);
                manager.SetAccess(owner, denyGroup);
            }

            if (!manager.IsModified)
            {
                return;
            }

            if (!manager.Save())
            {
                warnings.Add("ACL РЅРµ СЃРѕС…СЂР°РЅРµРЅ (" + scope + ").");
                return;
            }

            executed++;
            logs.Add("ACL РїСЂРёРјРµРЅРµРЅ: " + scope + ".");
        }
        catch (InvalidOperationException ex) when (IsInheritedAccessError(ex))
        {
            warnings.Add("ACL РїСЂРѕРїСѓС‰РµРЅ (" + scope + "): РѕР±СЉРµРєС‚ РёСЃРїРѕР»СЊР·СѓРµС‚ РЅР°СЃР»РµРґСѓРµРјС‹Рµ РїСЂР°РІР° Рё РЅРµ РґР°РµС‚ РёС… РёР·РјРµРЅРёС‚СЊ. " + ex.Message);
        }
        catch (Exception ex)
        {
            warnings.Add("РћС€РёР±РєР° РїСЂРёРјРµРЅРµРЅРёСЏ ACL (" + scope + "): " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static List<UserReferenceObject> UniqueOwners(IEnumerable<UserReferenceObject> owners)
    {
        var result = new List<UserReferenceObject>();
        var seen = new HashSet<Guid>();
        if (owners == null) return result;

        foreach (var owner in owners)
        {
            if (owner != null && seen.Add(owner.Guid))
            {
                result.Add(owner);
            }
        }

        return result;
    }

    private static void TryRemove(AccessManager manager, UserReferenceObject owner, AccessType type)
    {
        try
        {
            manager.RemoveAccess(owner, type, AccessDirection.Default);
        }
        catch
        {
            // Ignore remove errors.
        }
    }

    private static AccessGroup SelectAccessGroup(ServerConnection connection, bool deny)
    {
        var type = AccessType.GetTypes().FirstOrDefault(x => x.AccessTypeID == AccessTypeID.Object || x.IsObject);
        var groups = type != null ? type.GetGroups(connection) : null;
        if (groups == null || groups.Count == 0)
        {
            connection.RefreshAccessGroups();
            groups = connection.AccessGroups;
        }

        if (groups == null || groups.Count == 0)
        {
            return null;
        }

        return groups.OrderByDescending(x => ScoreGroup(x, deny)).FirstOrDefault();
    }

    private static int ScoreGroup(AccessGroup group, bool deny)
    {
        var allowed = 0;
        var forbidden = 0;
        foreach (AccessGroupCommand cmd in group)
        {
            if (cmd.State == AccessCommandState.Allowed) allowed++;
            if (cmd.State == AccessCommandState.Forbidden) forbidden++;
        }

        var n = (group.Name ?? string.Empty).ToLowerInvariant();
        var denyHint = n.Contains("deny") || n.Contains("forbid") || n.Contains("Р·Р°РїСЂРµС‚") || n.Contains("РЅРµС‚ РґРѕСЃС‚СѓРїР°");
        var editHint = n.Contains("edit") || n.Contains("СЂРµРґР°РєС‚") || n.Contains("write");

        var score = deny ? forbidden * 10 - allowed * 5 : allowed * 10 - forbidden * 5;
        if (deny && denyHint) score += 200;
        if (!deny && editHint) score += 200;
        if (deny && editHint) score -= 100;
        if (!deny && denyHint) score -= 100;
        return score;
    }

    private static AssignmentState BuildAssignmentState(FileReference fileReference, FolderObject assignmentsRoot, ICollection<string> warnings)
    {
        var state = new AssignmentState();
        var root = assignmentsRoot;
        if (root == null)
        {
            warnings.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' РЅРµ РЅР°Р№РґРµРЅР°. Р Р°Р·РґР°С‡Р° Р·Р°РґР°РЅРёР№ РїСЂРѕРїСѓС‰РµРЅР°.");
            return state;
        }

        var childFolders = GetChildFolders(root)
            .Where(x => !string.IsNullOrWhiteSpace(x.Name.Value))
            .ToArray();

        var workFolders = childFolders
            .Where(x => WorkFolderRegex.IsMatch(x.Name.Value ?? string.Empty))
            .OrderBy(x => ParseOrder(x.Name.Value))
            .ToList();

        if (workFolders.Count == 0)
        {
            var fallbackFolders = childFolders
                .Where(x => GetChildFiles(x).Count > 0)
                .OrderBy(x => x.Name.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (fallbackFolders.Length > 0)
            {
                warnings.Add("Р’ РїР°РїРєРµ 'Р—Р°РґР°РЅРёСЏ' РЅРµ РЅР°Р№РґРµРЅС‹ РїРѕРґРїР°РїРєРё 'Р Р°Р±РѕС‚Р° N'. РСЃРїРѕР»СЊР·СѓСЋС‚СЃСЏ РІСЃРµ РїРѕРґРїР°РїРєРё СЃ С„Р°Р№Р»Р°РјРё.");
                workFolders.AddRange(fallbackFolders);
            }
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
            var rootFiles = GetChildFiles(root);
            if (rootFiles.Count > 0)
            {
                warnings.Add("Р¤Р°Р№Р»С‹ РЅР°Р№РґРµРЅС‹ РїСЂСЏРјРѕ РІ РєРѕСЂРЅРµ РїР°РїРєРё 'Р—Р°РґР°РЅРёСЏ'. РСЃРїРѕР»СЊР·СѓРµС‚СЃСЏ РµРґРёРЅС‹Р№ РЅР°Р±РѕСЂ Р±РµР· СЂР°Р·Р±РёРµРЅРёСЏ РїРѕ 'Р Р°Р±РѕС‚Р° N'.");
                state.WorkFolders.Add(new WorkFolderState { Folder = root, SourceFiles = rootFiles });
            }
        }

        if (state.WorkFolders.Count == 0)
        {
            warnings.Add("Р’ РїР°РїРєРµ 'Р—Р°РґР°РЅРёСЏ' РЅРµ РЅР°Р№РґРµРЅРѕ РёСЃС…РѕРґРЅС‹С… С„Р°Р№Р»РѕРІ РїРѕ РїР°РїРєР°Рј 'Р Р°Р±РѕС‚Р° N'.");
        }

        return state;
    }

    private static FolderObject FindAssignmentsRoot(FileReference fileReference, FolderObject filesRootFolder, ICollection<string> logs)
    {
        var directChild = FindDirectChildFolder(filesRootFolder, AssignmentsRootFolderName);
        if (directChild != null)
        {
            return directChild;
        }

        foreach (var candidate in AssignmentsRootCandidates)
        {
            var folder = TryFindFolderByPath(fileReference, candidate);
            if (folder != null)
            {
                if (!IsInsideFolder(folder, filesRootFolder))
                {
                    logs.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' РЅР°Р№РґРµРЅР° РїРѕ Р°Р»СЊС‚РµСЂРЅР°С‚РёРІРЅРѕРјСѓ РїСѓС‚Рё РІРЅРµ 'Р¤Р°Р№Р»С‹': '" + NormalizePath(folder.Path.Value) + "'.");
                    return folder;
                }

                if (!string.Equals(candidate, FilesRootFolder + "/" + AssignmentsRootFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    logs.Add("РџР°РїРєР° 'Р—Р°РґР°РЅРёСЏ' РЅР°Р№РґРµРЅР° РїРѕ Р°Р»СЊС‚РµСЂРЅР°С‚РёРІРЅРѕРјСѓ РїСѓС‚Рё '" + candidate + "'.");
                }

                return folder;
            }
        }

        return null;
    }

    private static FolderObject FindTopLevelFolderByName(FileReference fileReference, string folderName)
    {
        if (fileReference == null || string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

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

    private static FolderObject FindAccessibleTopLevelFolder(FileReference fileReference)
    {
        if (fileReference == null)
        {
            return null;
        }

        try
        {
            return fileReference.Objects
                .OfType<FolderObject>()
                .Where(static x => x != null && x.Parent == null)
                .OrderBy(static x => x.Name.Value, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static FolderObject TryResolveTopLevelFolder(FileReference fileReference, FolderObject folder)
    {
        if (fileReference == null || folder == null)
        {
            return null;
        }

        var path = NormalizePath(folder.Path.Value);
        if (path.Length == 0)
        {
            return null;
        }

        var slashIndex = path.IndexOf('/');
        var rootName = slashIndex >= 0 ? path.Substring(0, slashIndex) : path;
        return FindTopLevelFolderByName(fileReference, rootName) ?? TryFindFolderByPath(fileReference, rootName);
    }

    private static FolderObject FindDirectChildFolder(FolderObject parentFolder, string folderName)
    {
        if (parentFolder == null || string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        return GetChildFolders(parentFolder).FirstOrDefault(x => IsSame(x.Name.Value, folderName));
    }

    private static bool IsInsideFolder(FolderObject folder, FolderObject parentFolder)
    {
        if (folder == null || parentFolder == null)
        {
            return false;
        }

        var folderPath = NormalizePath(folder.Path.Value);
        var parentPath = NormalizePath(parentFolder.Path.Value);
        return folderPath.StartsWith(parentPath + "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(folderPath, parentPath, StringComparison.OrdinalIgnoreCase);
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
            if (absolute != null) return absolute;
        }

        foreach (var candidate in BuildPathCandidates(path))
        {
            var relative = TryFindByPathCore(fileReference.FindByRelativePath, candidate);
            if (relative != null) return relative;
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

    private static User FindUserByLogin(UserReference userReference, string login)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var direct = userReference.FindUser(login);
        if (direct != null)
        {
            return direct;
        }

        return userReference.GetAllUsers()
            .OfType<User>()
            .FirstOrDefault(x => IsSame(x.Login.Value, login));
    }

    private static bool IsDuplicateUserLoginError(Exception ex)
    {
        var text = (ex.Message ?? string.Empty).ToLowerInvariant();
        return text.Contains("\u0443\u0436\u0435 \u0441\u0443\u0449\u0435\u0441\u0442\u0432\u0443\u0435\u0442")
            || text.Contains("\u0443\u0436\u0435 \u0441\u043e\u0434\u0435\u0440\u0436\u0438\u0442 \u043e\u0431\u044a\u0435\u043a\u0442")
            || text.Contains("\u043b\u043e\u0433\u0438\u043d")
            || text.Contains("notunique")
            || text.Contains("already exists")
            || text.Contains("unique");
    }

    private static bool IsInheritedAccessError(InvalidOperationException ex)
    {
        var text = (ex.Message ?? string.Empty).ToLowerInvariant();
        return text.Contains("\u043d\u0430\u0441\u043b\u0435\u0434\u0443\u0435\u043c")
            || text.Contains("inherit");
    }

    private static int ParseOrder(string name)
    {
        var m = Regex.Match(name ?? string.Empty, @"\d+");
        int n;
        return m.Success && int.TryParse(m.Value, out n) ? n : int.MaxValue;
    }

    private static void AssignTasks(
        FileReference fileReference,
        FolderObject destinationFolder,
        AssignmentState state,
        TFlexProvisioningStudent student,
        ICollection<string> logs,
        ICollection<string> warnings,
        ref int executed)
    {
        foreach (var work in state.WorkFolders)
        {
            if (work.SourceFiles.Count == 0) continue;

            var selected = SelectLeastUsed(work.SourceFiles, state.UsageByGuid);
            if (selected == null) continue;

            string assignedName;
            string error;
            if (TryCopyTaskFile(fileReference, selected, destinationFolder, work.Folder.Name.Value, warnings, out assignedName, out error))
            {
                IncreaseUsage(state.UsageByGuid, selected.Guid);
                executed++;
                logs.Add("РќР°Р·РЅР°С‡РµРЅРѕ Р·Р°РґР°РЅРёРµ '" + assignedName + "' СЃС‚СѓРґРµРЅС‚Сѓ '" + student.Login + "'.");
            }
            else
            {
                warnings.Add("РќРµ СѓРґР°Р»РѕСЃСЊ РЅР°Р·РЅР°С‡РёС‚СЊ Р·Р°РґР°РЅРёРµ СЃС‚СѓРґРµРЅС‚Сѓ '" + student.Login + "': " + error);
            }
        }
    }

    private static FileObject SelectLeastUsed(IReadOnlyList<FileObject> sourceFiles, IDictionary<Guid, int> usageByGuid)
    {
        var grouped = sourceFiles.GroupBy(x => GetUsage(usageByGuid, x.Guid)).OrderBy(x => x.Key).FirstOrDefault();
        if (grouped == null) return null;
        var candidates = grouped.ToList();
        return candidates.Count == 0 ? null : candidates[Random.Next(candidates.Count)];
    }

    private static bool TryCopyTaskFile(
        FileReference fileReference,
        FileObject sourceFile,
        FolderObject destinationFolder,
        string workFolderName,
        ICollection<string> warnings,
        out string assignedFileName,
        out string error)
    {
        var baseName = NormalizeFileName(workFolderName);
        var sourceExt = NormalizeExt(Path.GetExtension(sourceFile.Name.Value));
        var candidates = BuildCandidateNames(baseName, sourceExt);
        var failures = new List<string>();
        var hadConvertFailure = false;

        foreach (var candidateName in candidates)
        {
            var path = NormalizePath(destinationFolder.Path.Value + "/" + candidateName);
            if (TryFindByPath(fileReference, path) is FileObject)
            {
                failures.Add("Р¤Р°Р№Р» '" + path + "' СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚.");
                continue;
            }

            string copyError;
            if (TryCopyViaImport(fileReference, sourceFile, destinationFolder, candidateName, out copyError))
            {
                assignedFileName = candidateName;
                error = string.Empty;

                if (IsCad(sourceExt) && !SameExt(candidateName, sourceExt))
                {
                    warnings.Add("CAD-РґРѕРєСѓРјРµРЅС‚ '" + sourceFile.Name.Value + "' СЌРєСЃРїРѕСЂС‚РёСЂРѕРІР°РЅ РєР°Рє '" + candidateName + "'.");
                }
                else if (IsCad(sourceExt) && hadConvertFailure)
                {
                    warnings.Add("CAD-СЌРєСЃРїРѕСЂС‚ TIFF/PDF РЅРµРґРѕСЃС‚СѓРїРµРЅ РґР»СЏ '" + sourceFile.Name.Value + "', РёСЃРїРѕР»СЊР·РѕРІР°РЅ fallback.");
                }

                return true;
            }

            failures.Add(candidateName + ": " + copyError);
            if (IsCad(sourceExt) && !SameExt(candidateName, sourceExt))
            {
                hadConvertFailure = true;
            }
        }

        assignedFileName = string.Empty;
        error = string.Join(" | ", failures.ToArray());
        return false;
    }

    private static List<string> BuildCandidateNames(string baseName, string sourceExt)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string ext)
        {
            var name = baseName + ext;
            if (seen.Add(name)) list.Add(name);
        }

        if (IsCad(sourceExt))
        {
            Add(".tiff");
            Add(".pdf");
        }

        Add(string.IsNullOrWhiteSpace(sourceExt) ? ".bin" : sourceExt);
        return list;
    }

    private static bool TryCopyViaImport(FileReference fileReference, FileObject sourceFile, FolderObject destinationFolder, string targetName, out string error)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tflex_prov_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, targetName);

        try
        {
            // DOCs API pattern: export source file to temp storage and import with ImportParameters.
            sourceFile.Export(tempFile, true);

            var imported = fileReference
                .AddFiles(new[] { tempFile }, CreateSingleFileImportParameters(destinationFolder))
                .OfType<FileObject>()
                .FirstOrDefault();

            if (imported == null)
            {
                error = "AddFiles returned no imported file.";
                return false;
            }

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
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private static ImportParameters CreateSingleFileImportParameters(FolderObject destinationFolder)
    {
        return new ImportParameters
        {
            DestinationFolder = destinationFolder,
            Recursive = false,
            CreateClasses = true,
            AutoCheckIn = true,
            UpdateExistingFiles = false
        };
    }

    private static bool IsCad(string ext) => CadExtensions.Contains(NormalizeExt(ext));
    private static bool SameExt(string fileName, string ext) => string.Equals(NormalizeExt(Path.GetExtension(fileName)), NormalizeExt(ext), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeExt(string ext) => string.IsNullOrWhiteSpace(ext) ? string.Empty : (ext.StartsWith(".") ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant());
    private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/').Trim('/');
    private static string DescribeConfiguration(BaseConfiguration configuration)
    {
        if (configuration == null)
        {
            return "<null>";
        }

        return "'" + (configuration.Name ?? "<unnamed>") + "' (GUID: " + configuration.Guid + ")";
    }

    private static void VerifyProvisioningResult(
        UserReference userReference,
        FileReference fileReference,
        UsersGroup targetGroup,
        FolderObject groupFolder,
        IReadOnlyList<TFlexProvisioningStudent> students,
        ICollection<string> logs,
        ICollection<string> warnings)
    {
        if (targetGroup == null)
        {
            throw new InvalidOperationException("Р’РµСЂРёС„РёРєР°С†РёСЏ РЅРµ РїСЂРѕР№РґРµРЅР°: С†РµР»РµРІР°СЏ РіСЂСѓРїРїР° РѕС‚СЃСѓС‚СЃС‚РІСѓРµС‚.");
        }

        if (groupFolder == null)
        {
            throw new InvalidOperationException("Р’РµСЂРёС„РёРєР°С†РёСЏ РЅРµ РїСЂРѕР№РґРµРЅР°: РїР°РїРєР° РіСЂСѓРїРїС‹ РѕС‚СЃСѓС‚СЃС‚РІСѓРµС‚.");
        }

        var groupFolderPath = NormalizePath(groupFolder.Path.Value);
        if (!(TryFindFolderByPath(fileReference, groupFolderPath) is FolderObject))
        {
            throw new InvalidOperationException("Р’РµСЂРёС„РёРєР°С†РёСЏ РЅРµ РїСЂРѕР№РґРµРЅР°: РїР°РїРєР° РіСЂСѓРїРїС‹ '" + groupFolderPath + "' РЅРµ РЅР°Р№РґРµРЅР° РїРѕРІС‚РѕСЂРЅС‹Рј РїРѕРёСЃРєРѕРј.");
        }

        var refreshedGroup = userReference.GetAllUsersGroup()
            .OfType<UsersGroup>()
            .FirstOrDefault(x => x.Guid == targetGroup.Guid) ?? targetGroup;
        var unconfirmedMembershipCount = 0;
        foreach (var student in students)
        {
            var user = FindUserByLogin(userReference, student.Login);
            if (user == null)
            {
                throw new InvalidOperationException("Р’РµСЂРёС„РёРєР°С†РёСЏ РЅРµ РїСЂРѕР№РґРµРЅР°: РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ '" + student.Login + "' РЅРµ РЅР°Р№РґРµРЅ РїРѕСЃР»Рµ РІС‹РїРѕР»РЅРµРЅРёСЏ.");
            }

            if (!IsMemberOfGroup(userReference, refreshedGroup, student.Login))
            {
                unconfirmedMembershipCount++;
                warnings.Add("Verification warning: membership for user '" + student.Login + "' in group '" + refreshedGroup.FullName.Value + "' is not confirmed by API in current session.");
            }

            var studentFolderPath = NormalizePath(groupFolderPath + "/" + student.FolderName);
            if (!(TryFindFolderByPath(fileReference, studentFolderPath) is FolderObject))
            {
                throw new InvalidOperationException("Р’РµСЂРёС„РёРєР°С†РёСЏ РЅРµ РїСЂРѕР№РґРµРЅР°: РїР°РїРєР° СЃС‚СѓРґРµРЅС‚Р° '" + studentFolderPath + "' РЅРµ РЅР°Р№РґРµРЅР°.");
            }

            var tasksFolderPath = NormalizePath(studentFolderPath + "/" + AssignmentsRootFolderName);
            if (!(TryFindFolderByPath(fileReference, tasksFolderPath) is FolderObject))
            {
                throw new InvalidOperationException("Р’РµСЂРёС„РёРєР°С†РёСЏ РЅРµ РїСЂРѕР№РґРµРЅР°: РїР°РїРєР° Р·Р°РґР°РЅРёР№ '" + tasksFolderPath + "' РЅРµ РЅР°Р№РґРµРЅР°.");
            }
        }

        if (unconfirmedMembershipCount > 0)
        {
            logs.Add("Verification completed: users and folders are present; group membership is not confirmed immediately for " + unconfirmedMembershipCount + " user(s).");
        }
        else
        {
            logs.Add("Verification completed: users, group membership and folders are present.");
        }
    }

    private static string NormalizeFileName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) value = WorkFolderBaseName;
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value;
    }

    private static List<FolderObject> GetChildFolders(FolderObject folder)
    {
        folder.Load(true);
        return folder.Children.OfType<FolderObject>().ToList();
    }

    private static bool TryAddMembershipByGroupLink(UsersGroup group, User user, out string error)
    {
        try
        {
            group.BeginChanges(true);
            if (!group.CanCreateChildLink(user))
            {
                TryCancelChanges(group);
                error = "CanCreateChildLink=false.";
                return false;
            }

            group.CreateChildLink(user);
            if (!group.EndChanges())
            {
                error = "EndChanges returned false.";
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
                error = "CanCreateParentLink=false.";
                return false;
            }

            user.CreateParentLink(group);
            if (!user.EndChanges())
            {
                error = "User.EndChanges returned false.";
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

    private static bool TryAddMembershipByApiLinkGroup(UsersGroup group, User user, out string error)
    {
        var failures = new List<string>();
        var candidates = GetMembershipLinkGroups(group, user).ToArray();
        if (candidates.Length == 0)
        {
            error = "No candidate link groups found.";
            return false;
        }

        foreach (var linkGroup in candidates)
        {
            try
            {
                group.BeginChanges(true);
                group.AddLinkedObject(linkGroup.Guid, user);

                if (!group.EndChanges())
                {
                    failures.Add("[" + linkGroup.Name + "] EndChanges returned false.");
                    continue;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (IsDuplicateRelationError(ex))
            {
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                TryCancelChanges(group);
                failures.Add("[" + linkGroup.Name + "] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        error = failures.Count == 0 ? "No relation link applied." : string.Join(" | ", failures);
        return false;
    }

    private static IEnumerable<ParameterGroup> GetMembershipLinkGroups(UsersGroup group, User user)
    {
        if (group == null || user == null)
        {
            yield break;
        }

        var seen = new HashSet<Guid>();
        var userReferenceId = user.Reference != null ? user.Reference.Id : 0;

        foreach (var linkGroup in EnumerateMembershipLinkGroups(group))
        {
            if (linkGroup == null || !seen.Add(linkGroup.Guid))
            {
                continue;
            }

            if (userReferenceId <= 0)
            {
                yield return linkGroup;
                continue;
            }

            var referenceInfo = linkGroup.ReferenceInfo;
            if (referenceInfo == null || referenceInfo.Id == userReferenceId)
            {
                yield return linkGroup;
            }
        }
    }

    private static IEnumerable<ParameterGroup> EnumerateMembershipLinkGroups(UsersGroup group)
    {
        ParameterGroupCollection toMany = null;
        ParameterGroupCollection toManyComplex = null;

        try
        {
            toMany = group.Links?.ToMany?.LinkGroups;
        }
        catch
        {
            // ignore
        }

        if (toMany != null)
        {
            foreach (var linkGroup in toMany)
            {
                yield return linkGroup;
            }
        }

        try
        {
            toManyComplex = group.Links?.ToManyToComplexHierarchy?.LinkGroups;
        }
        catch
        {
            // ignore
        }

        if (toManyComplex != null)
        {
            foreach (var linkGroup in toManyComplex)
            {
                yield return linkGroup;
            }
        }
    }

    private static bool IsDuplicateRelationError(Exception ex)
    {
        var text = (ex.Message ?? string.Empty).ToLowerInvariant();
        return text.Contains("\u0443\u0436\u0435")
            || text.Contains("already")
            || text.Contains("exists")
            || text.Contains("duplicate")
            || text.Contains("notunique");
    }

    private static bool TryAddMembershipBySetParent(User user, UsersGroup group, out string error)
    {
        try
        {
            user.BeginChanges(true);
            if (!user.CanSetParent(group))
            {
                TryCancelChanges(user);
                error = "CanSetParent=false.";
                return false;
            }

            if (!user.SetParent(group))
            {
                TryCancelChanges(user);
                error = "SetParent returned false.";
                return false;
            }

            if (!user.EndChanges())
            {
                error = "User.EndChanges returned false.";
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

    private static bool WaitForMembership(UserReference userReference, UsersGroup group, string login, int retries, int delayMs)
    {
        for (var i = 0; i < retries; i++)
        {
            if (IsMemberOfGroup(userReference, group, login))
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return IsMemberOfGroup(userReference, group, login);
    }

    private static bool IsMemberOfGroup(UserReference userReference, UsersGroup group, string login)
    {
        var refreshedGroup = userReference.GetAllUsersGroup()
            .OfType<UsersGroup>()
            .FirstOrDefault(x => x.Guid == group.Guid) ?? group;
        try
        {
            if (refreshedGroup.GetAllInternalUsers(true).Any(x => IsSame(x.Login.Value, login)))
            {
                return true;
            }
        }
        catch
        {
            // Ignore stale group cache and continue with parent/link checks.
        }

        var user = FindUserByLogin(userReference, login);
        if (user == null)
        {
            return false;
        }

        try
        {
            if (refreshedGroup.GetAllInternalUsersAndGroups(true).Any(x => x.Guid == user.Guid))
            {
                return true;
            }
        }
        catch
        {
            // Ignore and continue with link-based checks.
        }

        try
        {
            if (refreshedGroup.GetChildLink(user) != null)
            {
                return true;
            }
        }
        catch
        {
            // Ignore link check issues.
        }

        try
        {
            var linksFromGroup = refreshedGroup.GetChildLinks(user);
            if (linksFromGroup != null && linksFromGroup.Count > 0)
            {
                return true;
            }
        }
        catch
        {
            // Ignore link check issues.
        }

        try
        {
            if (user.Parent != null && user.Parent.Guid == refreshedGroup.Guid)
            {
                return true;
            }
        }
        catch
        {
            // Ignore parent check issues.
        }

        try
        {
            if (user.GetParentLink(refreshedGroup) != null)
            {
                return true;
            }
        }
        catch
        {
            // Ignore link check issues.
        }

        try
        {
            var links = user.GetParentLinks(refreshedGroup);
            if (links != null && links.Count > 0)
            {
                return true;
            }
        }
        catch
        {
            // Ignore link enumeration issues.
        }

        if (HasMembershipRelationLink(refreshedGroup, user))
        {
            return true;
        }

        return false;
    }

    private static bool HasMembershipRelationLink(UsersGroup group, User user)
    {
        if (group == null || user == null)
        {
            return false;
        }

        foreach (var linkGroup in GetMembershipLinkGroups(group, user))
        {
            try
            {
                var relation = group.Links.ToMany[linkGroup];
                if (relation != null && relation.Objects != null && relation.Objects.Any(x => x != null && x.Guid == user.Guid))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                var relation = group.Links.ToManyToComplexHierarchy[linkGroup];
                if (relation != null && relation.Objects != null && relation.Objects.Any(x => x != null && x.Guid == user.Guid))
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }

        return false;
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

    private static string FirstLetter(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Substring(0, 1);
    private static bool IsSame(string left, string right) => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    private static int GetUsage(IDictionary<Guid, int> usageByGuid, Guid guid) => usageByGuid.TryGetValue(guid, out var value) ? value : 0;
    private static void IncreaseUsage(IDictionary<Guid, int> usageByGuid, Guid guid) => usageByGuid[guid] = GetUsage(usageByGuid, guid) + 1;

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
}

internal static class ConnectionFactory
{
    public static ServerConnection OpenConnection(TFlexConnectionRequest options, List<string> diagnostics)
    {
        var serializers = BuildSerializerOrder(ParseDataSerializer(options.DataSerializerAlgorithm, diagnostics));
        var compression = ParseCompression(options.CompressionAlgorithm);
        var modeOrder = BuildModeOrder(ParseCommunication(options.CommunicationMode));

        Exception lastException = null;
        foreach (var mode in modeOrder)
        {
            foreach (var serializer in serializers)
            {
                try
                {
                    diagnostics.Add("Connection attempt: " + mode + ", serializer=" + serializer + ", compression=" + compression + ".");

                    if (options.UseAccessToken)
                    {
                        return ServerConnection.OpenWithToken(
                            options.Server,
                            options.AccessToken,
                            options.ConfigurationGuid,
                            mode,
                            serializer,
                            compression,
                            proxy: null);
                    }

                    return ServerConnection.Open(
                        options.UserName,
                        new MD5HashString(options.Password, encrypt: true),
                        options.Server,
                        options.ConfigurationGuid,
                        mode,
                        serializer,
                        compression,
                        proxy: null);
                }
                catch (Exception ex)
                {
                    diagnostics.Add("Connection attempt failed for mode " + mode + " (serializer=" + serializer + "): " + ex.GetType().Name + ": " + ex.Message);
                    lastException = ex;
                }
            }
        }

        if (lastException != null)
        {
            throw lastException;
        }

        throw new InvalidOperationException("Failed to establish connection.");
    }

    public static List<string> ValidateOptions(TFlexConnectionRequest options)
    {
        var errors = new List<string>();
        if (options == null)
        {
            errors.Add("Connection options are null.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(options.Server))
        {
            errors.Add("Server is required.");
        }

        if (options.UseAccessToken)
        {
            if (string.IsNullOrWhiteSpace(options.AccessToken))
            {
                errors.Add("AccessToken is required when UseAccessToken=true.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.UserName))
            {
                errors.Add("UserName is required when UseAccessToken=false.");
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                errors.Add("Password is required when UseAccessToken=false.");
            }
        }

        return errors;
    }

    public static void AppendExceptionChain(ICollection<string> diagnostics, Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            diagnostics.Add(current.GetType().Name + ": " + current.Message);
            current = current.InnerException;
        }
    }

    private static CommunicationMode ParseCommunication(string value)
    {
        CommunicationMode mode;
        return Enum.TryParse(value, true, out mode) ? mode : CommunicationMode.GRPC;
    }

    private static IEnumerable<CommunicationMode> BuildModeOrder(CommunicationMode preferred)
    {
        yield return preferred;
        if (preferred == CommunicationMode.GRPC)
        {
            yield return CommunicationMode.WCF;
        }
        else
        {
            yield return CommunicationMode.GRPC;
        }
    }

    private static DataSerializerAlgorithm ParseDataSerializer(string value, ICollection<string> diagnostics)
    {
        DataSerializerAlgorithm algorithm;
        if (Enum.TryParse(value, true, out algorithm))
        {
            return algorithm;
        }

        diagnostics.Add("Unknown serializer value, fallback to 'Default'.");
        return DataSerializerAlgorithm.Default;
    }

    private static IReadOnlyList<DataSerializerAlgorithm> BuildSerializerOrder(DataSerializerAlgorithm preferred)
    {
        var result = new List<DataSerializerAlgorithm> { preferred };
        if (preferred == DataSerializerAlgorithm.Protobuf)
        {
            result.Add(DataSerializerAlgorithm.Default);
        }
        else if (preferred == DataSerializerAlgorithm.ZeroFormatter)
        {
            result.Add(DataSerializerAlgorithm.Default);
        }

        return result.Distinct().ToArray();
    }

    private static CompressionAlgorithm ParseCompression(string value)
    {
        CompressionAlgorithm algorithm;
        return Enum.TryParse(value, true, out algorithm) ? algorithm : CompressionAlgorithm.None;
    }
}

internal static class AssemblyBootstrap
{
    private static int _initialized;
    private static IReadOnlyList<string> _probeDirectories = Array.Empty<string>();

    public static void Initialize(string baseDirectory)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        _probeDirectories = BuildProbeDirectories(baseDirectory);
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        foreach (var directory in _probeDirectories)
        {
            TryInitResolver(directory);
        }
    }

    public static List<string> ValidateLocalAssemblySet()
    {
        var diagnostics = new List<string>();
        EnsureExists(diagnostics, "TFlex.DOCs.Model.dll");
        EnsureExists(diagnostics, "TFlex.DOCs.Common.dll");
        EnsureExists(diagnostics, "TFlex.DOCs.Data.dll");

        if (diagnostics.Count > 0)
        {
            diagnostics.Add("Probe directories: " + string.Join(", ", _probeDirectories));
        }

        return diagnostics;
    }

    private static IReadOnlyList<string> BuildProbeDirectories(string baseDirectory)
    {
        var candidates = new List<string>
        {
            baseDirectory,
            Path.Combine(baseDirectory, "libs"),
            Path.Combine(baseDirectory, "libs", "17.5.4.0"),
            Path.Combine(baseDirectory, "libs", "17.5.4.187"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "libs", "17.5.4.0")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "libs", "17.5.4.187")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "libs"))
        };

        var tflexHome = Environment.GetEnvironmentVariable("TFLEX_DOCS_HOME");
        if (!string.IsNullOrWhiteSpace(tflexHome))
        {
            candidates.Add(tflexHome);
            candidates.Add(Path.Combine(tflexHome, "Program"));
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                 })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "T-FLEX DOCs*"))
                {
                    candidates.Add(dir);
                    candidates.Add(Path.Combine(dir, "Program"));
                }
            }
            catch
            {
                // Ignore inaccessible folders.
            }
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureExists(List<string> diagnostics, string fileName)
    {
        if (!_probeDirectories.Any(directory => File.Exists(Path.Combine(directory, fileName))))
        {
            diagnostics.Add("Missing dependency: " + fileName);
        }
    }

    private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        var simpleName = new AssemblyName(args.Name).Name;
        var fileName = simpleName + ".dll";

        foreach (var directory in _probeDirectories)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
    }

    private static void TryInitResolver(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            AssemblyResolver.Instance.AddDirectory(directory);
        }
        catch
        {
            // Continue with AppDomain.AssemblyResolve fallback.
        }
    }
}

[DataContract]
internal sealed class TFlexConnectionRequest
{
    [DataMember(Name = "server")] public string Server { get; set; }
    [DataMember(Name = "userName")] public string UserName { get; set; }
    [DataMember(Name = "password")] public string Password { get; set; }
    [DataMember(Name = "useAccessToken")] public bool UseAccessToken { get; set; }
    [DataMember(Name = "accessToken")] public string AccessToken { get; set; }
    [DataMember(Name = "configurationGuid")] public Guid? ConfigurationGuid { get; set; }
    [DataMember(Name = "communicationMode")] public string CommunicationMode { get; set; }
    [DataMember(Name = "dataSerializerAlgorithm")] public string DataSerializerAlgorithm { get; set; }
    [DataMember(Name = "compressionAlgorithm")] public string CompressionAlgorithm { get; set; }
    [DataMember(Name = "folderCreationMacroName")] public string FolderCreationMacroName { get; set; }
}

[DataContract]
internal sealed class TFlexConnectionCheckResult
{
    [DataMember(Name = "success")] public bool Success { get; set; }
    [DataMember(Name = "message")] public string Message { get; set; }
    [DataMember(Name = "serverVersion")] public string ServerVersion { get; set; }
    [DataMember(Name = "isAdministrator")] public bool? IsAdministrator { get; set; }
    [DataMember(Name = "missingDependencies")] public string[] MissingDependencies { get; set; } = Array.Empty<string>();

    public static TFlexConnectionCheckResult Fail(string message, params string[] diagnostics)
    {
        return new TFlexConnectionCheckResult
        {
            Success = false,
            Message = message,
            MissingDependencies = diagnostics ?? Array.Empty<string>()
        };
    }
}

[DataContract]
internal sealed class TFlexProvisioningExecuteRequest
{
    [DataMember(Name = "connection")] public TFlexConnectionRequest Connection { get; set; }
    [DataMember(Name = "groupName")] public string GroupName { get; set; }
    [DataMember(Name = "students")] public List<TFlexProvisioningStudent> Students { get; set; } = new List<TFlexProvisioningStudent>();
    [DataMember(Name = "plannedActions")] public int PlannedActions { get; set; }
    [DataMember(Name = "assignTasks")] public bool? AssignTasks { get; set; }
}

[DataContract]
internal sealed class TFlexProvisioningStudent
{
    [DataMember(Name = "lastName")] public string LastName { get; set; }
    [DataMember(Name = "firstName")] public string FirstName { get; set; }
    [DataMember(Name = "middleName")] public string MiddleName { get; set; }
    [DataMember(Name = "login")] public string Login { get; set; }
    [DataMember(Name = "pinCode")] public string PinCode { get; set; }
    [DataMember(Name = "folderName")] public string FolderName { get; set; }
}

[DataContract]
internal sealed class TFlexProvisioningExecuteResult
{
    [DataMember(Name = "success")] public bool Success { get; set; }
    [DataMember(Name = "message")] public string Message { get; set; }
    [DataMember(Name = "plannedActions")] public int PlannedActions { get; set; }
    [DataMember(Name = "executedActions")] public int ExecutedActions { get; set; }
    [DataMember(Name = "logs")] public string[] Logs { get; set; } = Array.Empty<string>();
    [DataMember(Name = "warnings")] public string[] Warnings { get; set; } = Array.Empty<string>();
    [DataMember(Name = "missingDependencies")] public string[] MissingDependencies { get; set; } = Array.Empty<string>();

    public static TFlexProvisioningExecuteResult Fail(string message, params string[] diagnostics)
    {
        return new TFlexProvisioningExecuteResult
        {
            Success = false,
            Message = message,
            MissingDependencies = diagnostics ?? Array.Empty<string>(),
            Logs = Array.Empty<string>(),
            Warnings = Array.Empty<string>()
        };
    }
}
