using EngGraphLabAdminApp.Models;
using EngGraphLabAdminApp.Options;
using Microsoft.Extensions.Options;
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

        try
        {
            var communication = ParseCommunication(options.CommunicationMode);
            var serializer = ParseDataSerializer(options.DataSerializerAlgorithm);
            var compression = ParseCompression(options.CompressionAlgorithm);

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
            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: "T-FLEX API initialization failed. Most likely missing dependent DLLs.",
                ServerVersion: null,
                IsAdministrator: null,
                MissingDependencies: messages));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new TFlexConnectionCheckResult(
                Success: false,
                Message: $"Connection error: {ex.Message}",
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
}
