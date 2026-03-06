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
            return null;
        }

        if (!Directory.Exists(clientProgramDirectory))
        {
            return $"T-FLEX client directory was not found: {clientProgramDirectory}";
        }

        var resolverAssemblyPath = Path.Combine(clientProgramDirectory, "TFlex.PdmFramework.Resolve.dll");
        if (!File.Exists(resolverAssemblyPath))
        {
            return $"Resolver DLL is missing: {resolverAssemblyPath}";
        }

        try
        {
            var assembly = Assembly.LoadFrom(resolverAssemblyPath);
            var resolverType = assembly.GetType("TFlex.PdmFramework.Resolve.AssemblyResolver", throwOnError: false);
            if (resolverType is null)
            {
                return "Type TFlex.PdmFramework.Resolve.AssemblyResolver was not found.";
            }

            var instanceProperty = resolverType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var addDirectoryMethod = resolverType.GetMethod("AddDirectory", BindingFlags.Public | BindingFlags.Instance);
            var instance = instanceProperty?.GetValue(null);

            if (instance is null || addDirectoryMethod is null)
            {
                return "AssemblyResolver instance or AddDirectory method was not found.";
            }

            addDirectoryMethod.Invoke(instance, [clientProgramDirectory]);
            return null;
        }
        catch (Exception ex)
        {
            return $"AssemblyResolver initialization error: {ex.Message}";
        }
    }
}
