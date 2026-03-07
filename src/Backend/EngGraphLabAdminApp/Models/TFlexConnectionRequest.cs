namespace EngGraphLabAdminApp.Models;

public sealed class TFlexConnectionRequest
{
    public string Server { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseAccessToken { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public Guid? ConfigurationGuid { get; set; }
    public string CommunicationMode { get; set; } = "GRPC";
    public string DataSerializerAlgorithm { get; set; } = "Default";
    public string CompressionAlgorithm { get; set; } = "None";
    public string FolderCreationMacroName { get; set; } = string.Empty;
}
