using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public sealed class ProvisioningService(ITFlexConnectionService connectionService) : IProvisioningService
{
    private readonly ITFlexConnectionService _connectionService = connectionService;

    public ProvisioningPlan BuildPlan(string groupName, IReadOnlyList<StudentImportRow> students, bool includeTaskDistribution)
    {
        var actions = new List<ProvisioningAction>
        {
            new(
                Step: "EnsureUsersGroup",
                Target: $"Группы и пользователи/Студенты/{groupName}",
                Details: "Создать группу при отсутствии или использовать существующую.")
        };

        foreach (var student in students)
        {
            actions.Add(new ProvisioningAction(
                Step: "EnsureUser",
                Target: student.Login,
                Details: $"Создать пользователя типа 'Сотрудник' или использовать существующего. PIN={student.PinCode}."));

            actions.Add(new ProvisioningAction(
                Step: "AddUserToGroup",
                Target: student.Login,
                Details: $"Добавить в группу '{groupName}'."));

            actions.Add(new ProvisioningAction(
                Step: "EnsureGroupFolder",
                Target: $"Файлы/Студенты/{groupName}",
                Details: "Создать папку группы и подготовить права доступа для группы и преподавателей."));

            actions.Add(new ProvisioningAction(
                Step: "EnsureStudentFolder",
                Target: $"Файлы/Студенты/{groupName}/{student.FolderName}",
                Details: "Создать рабочую папку студента и подготовить права доступа для студента и преподавателей."));

            actions.Add(new ProvisioningAction(
                Step: "EnsureStudentTasksFolder",
                Target: $"Файлы/Студенты/{groupName}/{student.FolderName}/Задания",
                Details: "Создать подпапку 'Задания' у студента и подготовить права доступа для студента и преподавателей."));

            if (includeTaskDistribution)
            {
                actions.Add(new ProvisioningAction(
                    Step: "AssignTasks",
                    Target: $"Файлы/Студенты/{groupName}/{student.FolderName}/Задания",
                    Details: "Положить по одному случайному заданию из каждой папки 'Работа N' с переименованием 'Работа N.<ext>'."));
            }
        }

        return new ProvisioningPlan(
            GroupName: groupName,
            StudentsCount: students.Count,
            Students: students,
            Actions: actions);
    }

    public Task<ProvisioningExecutionResult> ExecuteFoundationAsync(ProvisioningPlan plan, bool includeTaskDistribution, CancellationToken cancellationToken)
    {
        return _connectionService.ExecuteFoundationAsync(plan, includeTaskDistribution, cancellationToken);
    }
}
