using EngGraphLabAdminApp.Models;
using EngGraphLabAdminApp.Options;
using EngGraphLabAdminApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<TFlexOptions>(builder.Configuration.GetSection(TFlexOptions.SectionName));
builder.Services.AddScoped<ITFlexConnectionService, TFlexConnectionService>();
builder.Services.AddScoped<IStudentTableParser, StudentTableParser>();
builder.Services.AddScoped<IProvisioningService, ProvisioningService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

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
    communication = "CommunicationMode: WCF|GRPC",
    dataSerializer = "DataSerializerAlgorithm: Default|Protobuf|ZeroFormatter",
    compression = "CompressionAlgorithm: None|GZip|LZMA|LZ4|LZ4High|ZSTD",
    clientProgramDirectory = "T-FLEX DOCs client Program directory, for AssemblyResolver",
    api = "TFlex.DOCs OpenAPI via ServerConnection.Open/OpenWithToken + references API"
}));

app.MapGet("/api/tflex/config-view", (IOptions<TFlexOptions> options) =>
{
    var value = options.Value;
    return Results.Ok(new
    {
        value.Server,
        value.UserName,
        value.UseAccessToken,
        value.ConfigurationGuid,
        value.ClientProgramDirectory,
        value.CommunicationMode,
        value.DataSerializerAlgorithm,
        value.CompressionAlgorithm
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
        ClientProgramDirectory = request.ClientProgramDirectory,
        CommunicationMode = request.CommunicationMode,
        DataSerializerAlgorithm = request.DataSerializerAlgorithm,
        CompressionAlgorithm = request.CompressionAlgorithm
    };

    var result = await connectionService.CheckConnectionAsync(options, cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/provisioning/preview", async (
    [FromForm] IFormFile file,
    IStudentTableParser tableParser,
    IProvisioningService provisioningService,
    CancellationToken cancellationToken) =>
{
    var parseResult = await tableParser.ParseAsync(file, cancellationToken);
    if (!parseResult.Success)
    {
        return Results.BadRequest(parseResult);
    }

    var plan = provisioningService.BuildPlan(parseResult.GroupName, parseResult.Students);
    return Results.Ok(new
    {
        parse = parseResult,
        plan
    });
});

app.MapPost("/api/provisioning/execute", async (
    [FromForm] IFormFile file,
    [FromQuery] bool dryRun,
    IStudentTableParser tableParser,
    IProvisioningService provisioningService,
    ITFlexConnectionService connectionService,
    CancellationToken cancellationToken) =>
{
    var parseResult = await tableParser.ParseAsync(file, cancellationToken);
    if (!parseResult.Success)
    {
        return Results.BadRequest(parseResult);
    }

    var plan = provisioningService.BuildPlan(parseResult.GroupName, parseResult.Students);
    if (dryRun)
    {
        return Results.Ok(new
        {
            dryRun = true,
            parse = parseResult,
            plan
        });
    }

    var connectionResult = await connectionService.CheckConnectionAsync(cancellationToken);
    if (!connectionResult.Success)
    {
        return Results.BadRequest(new
        {
            message = "T-FLEX connection is not available. Execution is blocked.",
            connection = connectionResult,
            plan
        });
    }

    var execution = await provisioningService.ExecuteFoundationAsync(plan, cancellationToken);
    return Results.Ok(new
    {
        dryRun = false,
        parse = parseResult,
        plan,
        connection = connectionResult,
        execution
    });
});

app.MapFallbackToFile("index.html");

app.Run();
