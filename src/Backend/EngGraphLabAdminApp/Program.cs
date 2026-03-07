using EngGraphLabAdminApp.Models;
using EngGraphLabAdminApp.Options;
using EngGraphLabAdminApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<TFlexOptions>(builder.Configuration.GetSection(TFlexOptions.SectionName));
builder.Services.Configure<TFlexAdapterOptions>(builder.Configuration.GetSection(TFlexAdapterOptions.SectionName));

var configuredCorsOrigins = builder.Configuration
    .GetSection("Frontend:CorsOrigins")
    .Get<string[]>()?
    .Where(static origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var corsOrigins = configuredCorsOrigins is { Length: > 0 }
    ? configuredCorsOrigins
    : new[] { "http://localhost:3000", "https://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<ITFlexConnectionService, TFlexConnectionService>();
builder.Services.AddScoped<IStudentTableParser, StudentTableParser>();
builder.Services.AddScoped<IProvisioningService, ProvisioningService>();
builder.Services.AddSingleton<IPasswordExportService, PasswordExportService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("FrontendCors");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "EngGraphLabAdminApp",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/tflex/requirements", () => Results.Ok(new
{
    server = "T-FLEX DOCs server host[:port]",
    credentials = "Administrator login/password or access token",
    configuration = "Configuration GUID (optional if server has one configuration)",
    communication = "CommunicationMode: GRPC|WCF (executed by net48 adapter)",
    dataSerializer = "DataSerializerAlgorithm: Default|Protobuf|ZeroFormatter",
    compression = "CompressionAlgorithm: None|GZip|LZMA|LZ4|LZ4High|ZSTD",
    adapter = "Local TFlexDocsAdapter (net48) via local IPC (stdin/stdout process call)",
    adapterExecutable = "Configured via TFlexAdapter:ExecutablePath or auto-discovered near repository/build output",
    dllLoading = "Adapter auto-loads T-FLEX DLLs from src/Backend/libs and app directory"
}));

app.MapGet("/api/frontend/endpoints", () => Results.Ok(new
{
    health = "/api/health",
    tflexRequirements = "/api/tflex/requirements",
    tflexConfigView = "/api/tflex/config-view",
    tflexCheckConnection = "/api/tflex/check-connection",
    tflexCheckConnectionCustom = "/api/tflex/check-connection/custom",
    provisioningPreview = "/api/provisioning/preview",
    provisioningExecute = "/api/provisioning/execute?dryRun=true|false&assignTasks=true|false",
    provisioningPasswords = "/api/provisioning/passwords/{token}?format=csv|xlsx",
    provisioningSampleCsv = "/api/provisioning/sample-csv",
    filesystemTree = "/api/filesystem/tree?path=C:\\\\some\\\\directory&maxDepth=8",
    uploadFields = new
    {
        single = "file",
        multiple = "files[] or repeated files"
    },
    supportedFormats = new[] { ".csv", ".xml", ".xlsx" }
}));

app.MapGet("/api/filesystem/tree", (
    [FromQuery] string? path,
    [FromQuery] int? maxDepth) =>
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return Results.BadRequest(new { message = "Query parameter 'path' is required." });
    }

    string fullPath;
    try
    {
        fullPath = Path.GetFullPath(path);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = $"Invalid path: {ex.Message}" });
    }

    if (!Directory.Exists(fullPath))
    {
        return Results.NotFound(new { message = $"Directory not found: {fullPath}" });
    }

    var safeDepth = Math.Clamp(maxDepth ?? 8, 0, 20);
    var tree = BuildDirectoryTree(new DirectoryInfo(fullPath), depth: 0, maxDepth: safeDepth);
    return Results.Ok(new
    {
        rootPath = fullPath,
        maxDepth = safeDepth,
        tree
    });
});

app.MapGet("/api/provisioning/sample-csv", () =>
{
    const string sample = """
lastname;firstname;middlename;login
Ivanov;Ivan;Ivanovich;iivanov
Petrova;Anna;Sergeevna;apetrova
Sidorov;Petr;Nikolaevich;psidorov
Smirnova;Elena;Olegovna;esmirnova
""";

    return Results.File(
        Encoding.UTF8.GetBytes(sample),
        "text/csv; charset=utf-8",
        "M25-123.csv");
});

app.MapGet("/api/tflex/config-view", (IOptions<TFlexOptions> options, IOptions<TFlexAdapterOptions> adapterOptions) =>
{
    var value = options.Value;
    var adapter = adapterOptions.Value;
    return Results.Ok(new
    {
        value.Server,
        value.UserName,
        value.UseAccessToken,
        value.ConfigurationGuid,
        value.CommunicationMode,
        value.DataSerializerAlgorithm,
        value.CompressionAlgorithm,
        adapterExecutablePath = adapter.ExecutablePath,
        adapterTimeoutSeconds = adapter.RequestTimeoutSeconds
    });
});

app.MapPost("/api/tflex/check-connection", async (ITFlexConnectionService connectionService, CancellationToken cancellationToken) =>
{
    var result = await connectionService.CheckConnectionAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/tflex/check-connection/custom", async (
    [FromBody] TFlexConnectionRequest request,
    ITFlexConnectionService connectionService,
    CancellationToken cancellationToken) =>
{
    var options = new TFlexOptions
    {
        Server = request.Server,
        UserName = request.UserName,
        Password = request.Password,
        UseAccessToken = request.UseAccessToken,
        AccessToken = request.AccessToken,
        ConfigurationGuid = request.ConfigurationGuid,
        CommunicationMode = request.CommunicationMode,
        DataSerializerAlgorithm = request.DataSerializerAlgorithm,
        CompressionAlgorithm = request.CompressionAlgorithm
    };

    var result = await connectionService.CheckConnectionAsync(options, cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/provisioning/preview", async (
    HttpRequest request,
    IStudentTableParser tableParser,
    IProvisioningService provisioningService,
    IPasswordExportService passwordExportService,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "Content-Type must be multipart/form-data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var files = ResolveImportFiles(form);
    if (files.Count == 0)
    {
        return Results.BadRequest(new { message = "At least one file is required in multipart/form-data (file or files)." });
    }

    var groups = await BuildGroupsAsync(files, tableParser, provisioningService, passwordExportService, includeTaskDistribution: true, cancellationToken);
    if (!TryValidateGroups(groups, out var validationMessage))
    {
        return Results.BadRequest(BuildProvisioningResponse(groups, dryRun: true, message: validationMessage));
    }

    return Results.Ok(BuildProvisioningResponse(groups, dryRun: true, message: null));
}).DisableAntiforgery();

app.MapPost("/api/provisioning/execute", async (
    HttpRequest request,
    [FromQuery] bool dryRun,
    [FromQuery] bool? assignTasks,
    IStudentTableParser tableParser,
    IProvisioningService provisioningService,
    ITFlexConnectionService connectionService,
    IPasswordExportService passwordExportService,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "Content-Type must be multipart/form-data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var files = ResolveImportFiles(form);
    if (files.Count == 0)
    {
        return Results.BadRequest(new { message = "At least one file is required in multipart/form-data (file or files)." });
    }

    var includeTaskDistribution = assignTasks ?? true;
    var groups = await BuildGroupsAsync(files, tableParser, provisioningService, passwordExportService, includeTaskDistribution, cancellationToken);
    if (!TryValidateGroups(groups, out var validationMessage))
    {
        return Results.BadRequest(BuildProvisioningResponse(groups, dryRun, validationMessage));
    }

    if (dryRun)
    {
        return Results.Ok(BuildProvisioningResponse(groups, dryRun: true, message: null));
    }

    var connectionResult = await connectionService.CheckConnectionAsync(cancellationToken);
    if (!connectionResult.Success)
    {
        return Results.BadRequest(BuildProvisioningResponse(
            groups,
            dryRun: false,
            message: "T-FLEX connection is not available. Execution is blocked.",
            connection: connectionResult));
    }

    foreach (var group in groups)
    {
        if (group.Plan is null)
        {
            continue;
        }

        group.Execution = await provisioningService.ExecuteFoundationAsync(group.Plan, includeTaskDistribution, cancellationToken);
    }

    return Results.Ok(BuildProvisioningResponse(groups, dryRun: false, message: null, connection: connectionResult));
}).DisableAntiforgery();

app.MapGet("/api/provisioning/passwords/{token}", (
    string token,
    [FromQuery] string? format,
    IPasswordExportService passwordExportService) =>
{
    var resolvedFormat = ParsePasswordFormat(format);
    if (!passwordExportService.TryBuildFile(token, resolvedFormat, out var file, out var error))
    {
        return Results.NotFound(new { message = error });
    }

    return Results.File(file!.Content, file.ContentType, file.FileName);
});

static PasswordExportFormat ParsePasswordFormat(string? format)
{
    if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
    {
        return PasswordExportFormat.Xlsx;
    }

    return PasswordExportFormat.Csv;
}

static List<IFormFile> ResolveImportFiles(IFormCollection form)
{
    var files = new List<IFormFile>();
    files.AddRange(form.Files.GetFiles("files"));

    var single = form.Files.GetFile("file");
    if (single is not null)
    {
        files.Add(single);
    }

    if (files.Count == 0 && form.Files.Count > 0)
    {
        files.AddRange(form.Files);
    }

    return files
        .GroupBy(static f => $"{f.Name}|{f.FileName}|{f.Length}", StringComparer.Ordinal)
        .Select(static g => g.First())
        .ToList();
}

static async Task<List<GroupProvisioningState>> BuildGroupsAsync(
    IReadOnlyList<IFormFile> files,
    IStudentTableParser tableParser,
    IProvisioningService provisioningService,
    IPasswordExportService passwordExportService,
    bool includeTaskDistribution,
    CancellationToken cancellationToken)
{
    var groups = new List<GroupProvisioningState>(files.Count);
    foreach (var file in files)
    {
        var parse = await tableParser.ParseAsync(file, cancellationToken);
        var group = new GroupProvisioningState(file.FileName, parse);

        if (parse.Success)
        {
            group.Plan = provisioningService.BuildPlan(parse.GroupName, parse.Students, includeTaskDistribution);
            group.PasswordExport = passwordExportService.Store(parse.GroupName, parse.Students);
        }

        groups.Add(group);
    }

    return groups;
}

static bool TryValidateGroups(IReadOnlyList<GroupProvisioningState> groups, out string? message)
{
    if (groups.Count == 0)
    {
        message = "No import files were provided.";
        return false;
    }

    if (groups.Any(static g => !g.Parse.Success))
    {
        message = "One or more files contain parse errors. Fix all files and retry.";
        return false;
    }

    var duplicateGroupNames = groups
        .Select(static g => g.Parse.GroupName)
        .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
        .Where(static g => g.Count() > 1)
        .Select(static g => g.Key)
        .ToArray();

    if (duplicateGroupNames.Length > 0)
    {
        message = $"Duplicate group names detected across files: {string.Join(", ", duplicateGroupNames)}.";
        return false;
    }

    message = null;
    return true;
}

static object BuildProvisioningResponse(
    IReadOnlyList<GroupProvisioningState> groups,
    bool dryRun,
    string? message,
    TFlexConnectionCheckResult? connection = null)
{
    var studentsCount = groups.Sum(static g => g.Parse.Students.Count);
    var actionsCount = groups.Sum(static g => g.Plan?.Actions.Count ?? 0);
    var executionResults = groups
        .Where(static g => g.Execution is not null)
        .Select(static g => g.Execution!)
        .ToArray();

    var hasParseErrors = groups.Any(static g => !g.Parse.Success);
    var hasExecutionErrors = executionResults.Any(static r => !r.Success);
    var blockedByConnection = !dryRun && connection is { Success: false };
    var success = !hasParseErrors && !hasExecutionErrors && !blockedByConnection && groups.Count > 0;

    var firstGroup = groups.Count == 1 ? groups[0] : null;
    return new
    {
        success,
        dryRun,
        multiple = groups.Count > 1,
        filesCount = groups.Count,
        groupsCount = groups.Count,
        studentsCount,
        actionsCount,
        message,
        connection,
        groups = groups.Select(g => new
        {
            fileName = g.FileName,
            parse = g.Parse,
            plan = g.Plan,
            passwordExport = g.PasswordExport,
            execution = g.Execution
        }),
        parse = firstGroup?.Parse,
        plan = firstGroup?.Plan,
        passwordExport = firstGroup?.PasswordExport,
        execution = firstGroup?.Execution,
        summary = new
        {
            parsedFiles = groups.Count(g => g.Parse.Success),
            filesWithErrors = groups.Count(g => !g.Parse.Success),
            executedGroups = executionResults.Length,
            executionFailedGroups = executionResults.Count(static e => !e.Success)
        }
    };
}

static DirectoryTreeNode BuildDirectoryTree(DirectoryInfo directory, int depth, int maxDepth)
{
    var node = new DirectoryTreeNode
    {
        Name = directory.Name,
        FullPath = directory.FullName
    };

    try
    {
        node.Files = directory
            .EnumerateFiles()
            .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static file => new DirectoryFileNode
            {
                Name = file.Name,
                FullPath = file.FullName,
                SizeBytes = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc
            })
            .ToList();
    }
    catch (Exception ex)
    {
        node.Error = "Files read error: " + ex.Message;
        node.Files = [];
    }

    if (depth >= maxDepth)
    {
        return node;
    }

    try
    {
        node.Directories = directory
            .EnumerateDirectories()
            .OrderBy(static dir => dir.Name, StringComparer.OrdinalIgnoreCase)
            .Select(subDirectory =>
            {
                if ((subDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return new DirectoryTreeNode
                    {
                        Name = subDirectory.Name,
                        FullPath = subDirectory.FullName,
                        Error = "Skipped reparse point."
                    };
                }

                return BuildDirectoryTree(subDirectory, depth + 1, maxDepth);
            })
            .ToList();
    }
    catch (Exception ex)
    {
        node.Error = string.IsNullOrWhiteSpace(node.Error)
            ? "Directories read error: " + ex.Message
            : node.Error + " | Directories read error: " + ex.Message;
        node.Directories = [];
    }

    return node;
}

app.MapFallbackToFile("index.html");

app.Run();

sealed class GroupProvisioningState(string fileName, StudentImportParseResult parse)
{
    public string FileName { get; } = fileName;
    public StudentImportParseResult Parse { get; } = parse;
    public ProvisioningPlan? Plan { get; set; }
    public PasswordExportDescriptor? PasswordExport { get; set; }
    public ProvisioningExecutionResult? Execution { get; set; }
}

sealed class DirectoryTreeNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public List<DirectoryTreeNode> Directories { get; set; } = [];
    public List<DirectoryFileNode> Files { get; set; } = [];
    public string? Error { get; set; }
}

sealed class DirectoryFileNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastWriteUtc { get; set; }
}
