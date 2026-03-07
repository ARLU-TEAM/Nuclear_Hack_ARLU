using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EngGraphLabAdminApp.Models;
using EngGraphLabAdminApp.Options;
using Microsoft.Extensions.Options;

namespace EngGraphLabAdminApp.Services;

public sealed class TFlexConnectionService(
    IOptions<TFlexOptions> options,
    IOptions<TFlexAdapterOptions> adapterOptions,
    ILogger<TFlexConnectionService> logger) : ITFlexConnectionService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly TFlexOptions _options = options.Value;
    private readonly TFlexAdapterOptions _adapterOptions = adapterOptions.Value;
    private readonly ILogger<TFlexConnectionService> _logger = logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions RequestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task<TFlexConnectionCheckResult> CheckConnectionAsync(CancellationToken cancellationToken)
        => CheckConnectionAsync(_options, cancellationToken);

    public async Task<TFlexConnectionCheckResult> CheckConnectionAsync(TFlexOptions options, CancellationToken cancellationToken)
    {
        var diagnostics = ValidateOptions(options);
        if (diagnostics.Count > 0)
        {
            return new TFlexConnectionCheckResult(
                Success: false,
                Message: "TFlex configuration is incomplete.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: diagnostics);
        }

        var request = BuildConnectionRequest(options);
        var invocation = await InvokeAdapterAsync<TFlexConnectionRequest, TFlexConnectionCheckResult>(
            command: "check-connection",
            payload: request,
            cancellationToken: cancellationToken);

        var mergedDiagnostics = MergeDiagnostics(diagnostics, invocation.Diagnostics);
        if (invocation.Payload is null)
        {
            return new TFlexConnectionCheckResult(
                Success: false,
                Message: "TFlex adapter returned invalid payload.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: mergedDiagnostics);
        }

        var payload = invocation.Payload;
        mergedDiagnostics = MergeDiagnostics(mergedDiagnostics, payload.MissingDependencies);

        if (invocation.ExitCode is not 0)
        {
            return payload with
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(payload.Message)
                    ? "TFlex adapter process failed."
                    : payload.Message,
                MissingDependencies = mergedDiagnostics
            };
        }

        return payload with { MissingDependencies = mergedDiagnostics };
    }

    public async Task<ProvisioningExecutionResult> ExecuteFoundationAsync(ProvisioningPlan plan, bool includeTaskDistribution, CancellationToken cancellationToken)
    {
        var diagnostics = ValidateOptions(_options);
        if (diagnostics.Count > 0)
        {
            return new ProvisioningExecutionResult(
                Success: false,
                Message: "TFlex configuration is incomplete.",
                PlannedActions: plan.Actions.Count,
                ExecutedActions: 0,
                Logs: [],
                Warnings: [],
                Diagnostics: diagnostics);
        }

        if (string.IsNullOrWhiteSpace(plan.GroupName))
        {
            return new ProvisioningExecutionResult(
                Success: false,
                Message: "Group name is empty.",
                PlannedActions: plan.Actions.Count,
                ExecutedActions: 0,
                Logs: [],
                Warnings: [],
                Diagnostics: []);
        }

        if (plan.Students.Count == 0)
        {
            return new ProvisioningExecutionResult(
                Success: false,
                Message: "Students list is empty.",
                PlannedActions: plan.Actions.Count,
                ExecutedActions: 0,
                Logs: [],
                Warnings: [],
                Diagnostics: []);
        }

        var request = new TFlexProvisioningExecuteRequest
        {
            Connection = BuildConnectionRequest(_options),
            GroupName = plan.GroupName,
            PlannedActions = plan.Actions.Count,
            AssignTasks = includeTaskDistribution,
            Students = plan.Students
                .Select(student => new TFlexProvisioningStudent
                {
                    LastName = student.LastName,
                    FirstName = student.FirstName,
                    MiddleName = student.MiddleName,
                    Login = student.Login,
                    PinCode = student.PinCode,
                    FolderName = student.FolderName
                })
                .ToArray()
        };

        var invocation = await InvokeAdapterAsync<TFlexProvisioningExecuteRequest, TFlexProvisioningExecuteResult>(
            command: "execute-foundation",
            payload: request,
            cancellationToken: cancellationToken);

        var mergedDiagnostics = MergeDiagnostics(diagnostics, invocation.Diagnostics);
        if (invocation.Payload is null)
        {
            return new ProvisioningExecutionResult(
                Success: false,
                Message: "TFlex adapter returned invalid payload.",
                PlannedActions: plan.Actions.Count,
                ExecutedActions: 0,
                Logs: [],
                Warnings: [],
                Diagnostics: mergedDiagnostics);
        }

        var payload = invocation.Payload;
        mergedDiagnostics = MergeDiagnostics(mergedDiagnostics, payload.MissingDependencies);

        if (invocation.ExitCode is not 0)
        {
            return new ProvisioningExecutionResult(
                Success: false,
                Message: string.IsNullOrWhiteSpace(payload.Message)
                    ? "TFlex provisioning adapter failed."
                    : payload.Message,
                PlannedActions: payload.PlannedActions,
                ExecutedActions: payload.ExecutedActions,
                Logs: payload.Logs,
                Warnings: payload.Warnings,
                Diagnostics: mergedDiagnostics);
        }

        return new ProvisioningExecutionResult(
            Success: payload.Success,
            Message: payload.Message,
            PlannedActions: payload.PlannedActions,
            ExecutedActions: payload.ExecutedActions,
            Logs: payload.Logs,
            Warnings: payload.Warnings,
            Diagnostics: mergedDiagnostics);
    }

    private async Task<AdapterInvocation<TResponse>> InvokeAdapterAsync<TRequest, TResponse>(
        string command,
        TRequest payload,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var diagnostics = new List<string>();

        var candidatePaths = BuildAdapterCandidates();
        var adapterPath = ResolveAdapterPath(candidatePaths);
        if (adapterPath is null)
        {
            diagnostics.Add("TFlex adapter executable was not found.");
            diagnostics.AddRange(candidatePaths.Select(path => $"Checked: {path}"));
            diagnostics.Add("Build adapter project: dotnet build src/Backend/TFlexDocsAdapter/TFlexDocsAdapter.csproj");
            return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), -1);
        }

        try
        {
            _logger.LogInformation("Starting TFlex adapter command {Command}.", command);
            using var process = StartAdapterProcess(adapterPath, command);
            if (process is null)
            {
                diagnostics.Add($"Unable to start adapter process from '{adapterPath}'.");
                return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), -1);
            }

            var requestJson = JsonSerializer.Serialize(payload, RequestSerializerOptions);
            await process.StandardInput.WriteAsync(requestJson);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var timeout = TimeSpan.FromSeconds(NormalizeTimeoutSeconds(_adapterOptions.RequestTimeoutSeconds));
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, Task.Delay(timeout, cancellationToken));
            if (completedTask != waitTask)
            {
                TryTerminate(process);
                diagnostics.Add($"Adapter timeout after {timeout.TotalSeconds:F0}s.");
                diagnostics.Add($"Adapter path: {adapterPath}");
                diagnostics.Add(await SafeRead(stderrTask));
                return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), -1);
            }

            await waitTask;
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            _logger.LogInformation(
                "TFlex adapter command {Command} finished. ExitCode={ExitCode}, AdapterPath={AdapterPath}",
                command,
                process.ExitCode,
                adapterPath);

            if (string.IsNullOrWhiteSpace(stdout))
            {
                diagnostics.Add("Adapter returned empty response.");
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    diagnostics.Add($"Adapter stderr: {stderr.Trim()}");
                }

                return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), process.ExitCode);
            }

            TResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<TResponse>(stdout, SerializerOptions);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Cannot parse adapter JSON: {ex.GetType().Name}: {ex.Message}");
                diagnostics.Add($"Adapter stdout: {TrimForDiagnostics(stdout)}");
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    diagnostics.Add($"Adapter stderr: {stderr.Trim()}");
                }

                return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), process.ExitCode);
            }

            if (response is null)
            {
                diagnostics.Add("Adapter JSON was parsed as null.");
                return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), process.ExitCode);
            }

            if (process.ExitCode != 0)
            {
                AddUnique(diagnostics, $"Adapter exit code: {process.ExitCode}");
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    AddUnique(diagnostics, $"Adapter stderr: {stderr.Trim()}");
                }

                _logger.LogWarning(
                    "TFlex adapter command {Command} failed. ExitCode={ExitCode}. Stderr={Stderr}",
                    command,
                    process.ExitCode,
                    string.IsNullOrWhiteSpace(stderr) ? "<empty>" : stderr.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(stderr))
            {
                AddUnique(diagnostics, $"Adapter stderr: {stderr.Trim()}");
                _logger.LogInformation("TFlex adapter command {Command} stderr: {Stderr}", command, stderr.Trim());
            }

            return new AdapterInvocation<TResponse>(response, CleanupDiagnostics(diagnostics), process.ExitCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add("Adapter call was canceled.");
            return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), -1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TFlex adapter process call failed. Command: {Command}", command);
            diagnostics.Add($"Adapter command: {command}");
            AppendExceptionChain(diagnostics, ex);
            return new AdapterInvocation<TResponse>(null, CleanupDiagnostics(diagnostics), -1);
        }
    }

    private static TFlexConnectionRequest BuildConnectionRequest(TFlexOptions options)
    {
        return new TFlexConnectionRequest
        {
            Server = options.Server,
            UserName = options.UserName,
            Password = options.Password,
            UseAccessToken = options.UseAccessToken,
            AccessToken = options.AccessToken,
            ConfigurationGuid = options.ConfigurationGuid,
            CommunicationMode = options.CommunicationMode,
            DataSerializerAlgorithm = options.DataSerializerAlgorithm,
            CompressionAlgorithm = options.CompressionAlgorithm,
            FolderCreationMacroName = options.FolderCreationMacroName
        };
    }

    private Process? StartAdapterProcess(string adapterPath, string command)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = adapterPath,
            Arguments = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Process.Start(processInfo);
    }

    private IReadOnlyList<string> BuildAdapterCandidates()
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(_adapterOptions.ExecutablePath))
        {
            candidates.Add(Path.GetFullPath(_adapterOptions.ExecutablePath));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "TFlexDocsAdapter.exe"));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TFlexDocsAdapter", "bin", "Release", "net48", "TFlexDocsAdapter.exe")));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TFlexDocsAdapter", "bin", "Debug", "net48", "TFlexDocsAdapter.exe")));

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveAdapterPath(IEnumerable<string> candidates)
    {
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static int NormalizeTimeoutSeconds(int value)
    {
        if (value < 1)
        {
            return 30;
        }

        return Math.Min(value, 300);
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

    private static List<string> MergeDiagnostics(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var merged = new List<string>(left.Count + right.Count);
        foreach (var item in left)
        {
            AddUnique(merged, item);
        }

        foreach (var item in right)
        {
            AddUnique(merged, item);
        }

        return merged;
    }

    private static List<string> CleanupDiagnostics(IReadOnlyList<string> diagnostics)
    {
        var cleaned = new List<string>();
        foreach (var item in diagnostics)
        {
            AddUnique(cleaned, item);
        }

        return cleaned;
    }

    private static void AddUnique(List<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!target.Contains(value, StringComparer.Ordinal))
        {
            target.Add(value);
        }
    }

    private static string TrimForDiagnostics(string text)
    {
        const int max = 800;
        var value = text.Trim();
        if (value.Length <= max)
        {
            return value;
        }

        return value[..max] + "...";
    }

    private static async Task<string> SafeRead(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void AppendExceptionChain(List<string> diagnostics, Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            diagnostics.Add($"{current.GetType().Name}: {current.Message}");
            current = current.InnerException;
        }
    }

    private sealed record AdapterInvocation<TPayload>(
        TPayload? Payload,
        IReadOnlyList<string> Diagnostics,
        int ExitCode)
        where TPayload : class;
}
