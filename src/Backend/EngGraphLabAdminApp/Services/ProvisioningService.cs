using EngGraphLabAdminApp.Models;

namespace EngGraphLabAdminApp.Services;

public sealed class ProvisioningService : IProvisioningService
{
    public ProvisioningPlan BuildPlan(string groupName, IReadOnlyList<StudentImportRow> students)
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
                Details: "Создать папку группы и назначить доступ Редакторский группе и Преподавателям."));

            actions.Add(new ProvisioningAction(
                Step: "EnsureStudentFolder",
                Target: $"Файлы/Студенты/{groupName}/{student.FolderName}",
                Details: "Создать рабочую папку студента, выдать доступ Редакторский студенту и Преподавателям."));

            actions.Add(new ProvisioningAction(
                Step: "AssignTasks",
                Target: $"Файлы/Студенты/{groupName}/{student.FolderName}/Задания",
                Details: "Положить по одному случайному заданию из каждой папки 'Работа N' с переименованием 'Работа N.<ext>'."));
        }

        return new ProvisioningPlan(
            GroupName: groupName,
            StudentsCount: students.Count,
            Actions: actions);
    }

    public Task<ProvisioningExecutionResult> ExecuteFoundationAsync(ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return Task.FromResult(new ProvisioningExecutionResult(
            Success: true,
            Message: "Базовый каркас execution выполнен в режиме foundation. Реальные вызовы OpenAPI для users/files/rights/tasks подключаются в этом методе.",
            PlannedActions: plan.Actions.Count,
            ExecutedActions: 0));
    }
}
