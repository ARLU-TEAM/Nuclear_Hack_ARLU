using EngGraphLabAdminApp.Models;
using EngGraphLabAdminApp.Options;
using Microsoft.Extensions.Options;
using System.Reflection;
using TFlex.DOCs.Common;
using TFlex.DOCs.Common.Encryption;
using TFlex.DOCs.Model;

namespace EngGraphLabAdminApp.Services;

public sealed class TFlexConnectionService(IOptions<TFlexOptions> options) : ITFlexConnectionService
{
    private readonly TFlexOptions _options = options.Value;

    public Task<TFlexConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken)
        => CheckConnectionAsync(_options, cancellationToken);

    public Task<TFlexConnectionCheckResult> CheckConnectionAsync(TFlexOptions options, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var messages = ValidateOptions(options);
        if (messages.Count > 0)
        {
            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: "TFlex configuration is incomplete.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: messages));
        }

        var resolverMessage = TFlexAssemblyResolverBootstrap.TryInitialize(options.ClientProgramDirectory);
        if (!string.IsNullOrWhiteSpace(resolverMessage))
        {
            messages.Add(resolverMessage);
        }

        var assemblySetIssue = ValidateLocalAssemblySet();
        if (!string.IsNullOrWhiteSpace(assemblySetIssue))
        {
            messages.Add(assemblySetIssue);
            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: "T-FLEX DLL set is inconsistent.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: messages));
        }

        try
        {
            var communication = ParseCommunication(options.CommunicationMode);
            var serializer = ParseDataSerializer(options.DataSerializerAlgorithm);
            var compression = ParseCompression(options.CompressionAlgorithm);

            if (communication == CommunicationMode.WCF)
            {
                messages.Add("WCF mode is not supported in this backend target (net8). Use GRPC.");
                return Task.FromResult(new TFlexConnectionCheckResult(
                    Success: false,
                    Message: "Unsupported communication mode.",
                    ServerVersion: null,
                    IsAdministrator: null,
                    MissingDependencies: messages));
            }

            var connection = options.UseAccessToken
                ? ServerConnection.OpenWithToken(
                    options.Server,
                    options.AccessToken,
                    options.ConfigurationGuid,
                    communication,
                    serializer,
                    compression,
                    proxy: null)
                : ServerConnection.Open(
                    options.UserName,
                    new MD5HashString(options.Password, encrypt: true),
                    options.Server,
                    options.ConfigurationGuid,
                    communication,
                    serializer,
                    compression,
                    proxy: null);

            using (connection)
            {
                var version = connection.Version?.ToString();
                var isAdmin = connection.IsAdministrator;
                connection.Close();

                return Task.FromResult(new TFlexConnectionCheckResult(
                    Success: true,
                    Message: "Connected to T-FLEX DOCs.",
                    ServerVersion: version,
                    IsAdministrator: isAdmin,
                    MissingDependencies: messages));
            }
        }
        catch (FileNotFoundException ex)
        {
            messages.Add(ex.Message);
            AppendExceptionChain(messages, ex);
            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: "Required T-FLEX dependent DLLs are missing.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: messages));
        }
        catch (TypeInitializationException ex)
        {
            messages.Add(ex.InnerException?.Message ?? ex.Message);
            AppendExceptionChain(messages, ex);

            var isGrpcRuntimeIncompatible =
                ExceptionChainContains(ex, "TFlex.DOCs.GRPC.Formatters.DynamicAssemblyHolder") &&
                ExceptionChainContains(ex, "Illegal enum value: 3");

            if (isGrpcRuntimeIncompatible)
            {
                messages.Add("Current runtime is net8. T-FLEX GRPC formatter in this DLL set expects .NET Framework behavior.");
                messages.Add("Recommended: run T-FLEX integration adapter on .NET Framework 4.7.2/4.8 and call it from this web app.");

                return Task.FromResult(new TFlexConnectionCheckResult(
                    Success: false,
                    Message: "T-FLEX GRPC runtime incompatibility with net8.",
                    ServerVersion: null,
                    IsAdministrator: null,
                    MissingDependencies: messages));
            }

            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: "T-FLEX API initialization failed. Most likely missing dependent DLLs.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: messages));
        }
        catch (Exception ex)
        {
            AppendExceptionChain(messages, ex);
            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: $"Connection error: {ex.GetType().Name}: {ex.Message}",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: messages));
        }
    }

    private static CommunicationMode ParseCommunication(string value)
    {
        if (Enum.TryParse<CommunicationMode>(value, true, out var mode))
        {
            return mode;
        }

        return DataFormatterSettings.DefaultCommunicationMode;
    }

    private static DataSerializerAlgorithm ParseDataSerializer(string value)
    {
        if (Enum.TryParse<DataSerializerAlgorithm>(value, true, out var algorithm))
        {
            return algorithm;
        }

        return DataFormatterSettings.DefaultDataSerializerAlgorithm;
    }

    private static CompressionAlgorithm ParseCompression(string value)
    {
        if (Enum.TryParse<CompressionAlgorithm>(value, true, out var algorithm))
        {
            return algorithm;
        }

        return DataFormatterSettings.DefaultCompressionAlgorithm;
    }

    private static List<string> ValidateOptions(TFlexOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Server))
        {
            errors.Add("TFlex:Server is not set.");
        }

        if (options.UseAccessToken)
        {
            if (string.IsNullOrWhiteSpace(options.AccessToken))
            {
                errors.Add("TFlex:AccessToken is required when UseAccessToken=true.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.UserName))
            {
                errors.Add("TFlex:UserName is not set.");
            }

            if (string.IsNullOrWhiteSpace(options.Password))
            {
                errors.Add("TFlex:Password is not set.");
            }
        }

        return errors;
    }

    private static string? ValidateLocalAssemblySet()
    {
        var baseDir = AppContext.BaseDirectory;

        var modelPath = Path.Combine(baseDir, "TFlex.DOCs.Model.dll");
        var commonPath = Path.Combine(baseDir, "TFlex.DOCs.Common.dll");
        var dataPath = Path.Combine(baseDir, "TFlex.DOCs.Data.dll");
        var dataMailPath = Path.Combine(baseDir, "TFlex.DOCs.Data.Mail.dll");
        var tasksExtPath = Path.Combine(baseDir, "System.Threading.Tasks.Extensions.dll");
        var chilkatPath = Path.Combine(baseDir, "ChilkatDotNet47.dll");

        if (!File.Exists(modelPath) || !File.Exists(commonPath) || !File.Exists(dataPath))
        {
            return "One or more core DLLs are missing in output folder: TFlex.DOCs.Model/Common/Data.";
        }

        if (!File.Exists(dataMailPath))
        {
            return "Missing dependency: TFlex.DOCs.Data.Mail.dll";
        }

        if (!File.Exists(tasksExtPath))
        {
            return "Missing dependency: System.Threading.Tasks.Extensions.dll (required version 4.2.0.1).";
        }

        if (!File.Exists(chilkatPath))
        {
            return "Missing dependency: ChilkatDotNet47.dll (required by TFlex.DOCs.Data.Mail).";
        }

        var modelVersion = AssemblyName.GetAssemblyName(modelPath).Version;
        var commonVersion = AssemblyName.GetAssemblyName(commonPath).Version;
        var dataVersion = AssemblyName.GetAssemblyName(dataPath).Version;

        if (modelVersion != commonVersion || modelVersion != dataVersion)
        {
            return $"DLL versions mismatch. Model={modelVersion}; Common={commonVersion}; Data={dataVersion}. Use one consistent set.";
        }

        return null;
    }

    private static void AppendExceptionChain(List<string> messages, Exception ex)
    {
        var current = ex.InnerException;
        while (current is not null)
        {
            messages.Add($"{current.GetType().Name}: {current.Message}");
            current = current.InnerException;
        }
    }

    private static bool ExceptionChainContains(Exception ex, string text)
    {
        if (Contains(ex.Message, text))
        {
            return true;
        }

        var current = ex.InnerException;
        while (current is not null)
        {
            if (Contains(current.Message, text))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool Contains(string source, string value) =>
        source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
}
