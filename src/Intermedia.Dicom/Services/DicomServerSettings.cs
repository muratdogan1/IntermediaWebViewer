namespace Intermedia.Dicom.Services;

public class DicomServerSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 104;
    public string AeTitle { get; set; } = "interMEDIAPacs";

    // bizim uygulama AE + port (C-STORE SCP)
    public string LocalAeTitle { get; set; } = "LOCALSTORAGE";
    public string MoveDestinationAeTitle { get; set; } = "LOCALSTORAGE";
    public int LocalPort { get; set; } = 11112;

    public string StorageFolder { get; set; } = @"C:\DicomCache";
}