using System.Reflection;
using System.Threading;

namespace EngGraphLabAdminApp.Services;

internal static class TFlexAssemblyResolverBootstrap
{
    private static int _initialized;

    public static string? TryInitialize(string clientProgramDirectory)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(clientProgramDirectory))
        {
            return "ClientProgramDirectory не задан. Встроенный AssemblyResolver не активирован.";
        }

        if (!Directory.Exists(clientProgramDirectory))
        {
            return $"Папка клиента T-FLEX DOCs не найдена: {clientProgramDirectory}";
        }

        var resolverAssemblyPath = Path.Combine(clientProgramDirectory, "TFlex.PdmFramework.Resolve.dll");
        if (!File.Exists(resolverAssemblyPath))
        {
            return $"Не найден файл {resolverAssemblyPath}. Установите T-FLEX DOCs клиент или поправьте путь.";
        }

        try
        {
            var assembly = Assembly.LoadFrom(resolverAssemblyPath);
            var resolverType = assembly.GetType("TFlex.PdmFramework.Resolve.AssemblyResolver", throwOnError: false);
            if (resolverType is null)
            {
                return "Тип TFlex.PdmFramework.Resolve.AssemblyResolver не найден.";
            }

            var instanceProperty = resolverType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var addDirectoryMethod = resolverType.GetMethod("AddDirectory", BindingFlags.Public | BindingFlags.Instance);
            var instance = instanceProperty?.GetValue(null);

            if (instance is null || addDirectoryMethod is null)
            {
                return "AssemblyResolver найден, но не удалось получить Instance/AddDirectory.";
            }

            addDirectoryMethod.Invoke(instance, [clientProgramDirectory]);
            return null;
        }
        catch (Exception ex)
        {
            return $"Ошибка инициализации AssemblyResolver: {ex.Message}";
        }
    }
}
