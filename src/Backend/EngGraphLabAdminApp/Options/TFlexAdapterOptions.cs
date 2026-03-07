namespace EngGraphLabAdminApp.Options;

public sealed class TFlexAdapterOptions
{
    public const string SectionName = "TFlexAdapter";

    public string ExecutablePath { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
}
