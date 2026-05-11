using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Intermedia.Dicom.Services;

public sealed class StorageScpHosted
{
    private readonly IServiceProvider _sp;
    private readonly DicomServerSettings _settings;
    private IDicomServer? _server;

    public StorageScpHosted(IServiceProvider sp, DicomServerSettings settings)
    {
        _sp = sp;
        _settings = settings;
    }

    public void Start()
    {
        Directory.CreateDirectory(_settings.StorageFolder);

        StorageScpService.StorageFolder = _settings.StorageFolder;

        var factory = _sp.GetRequiredService<IDicomServerFactory>();
        _server = factory.Create<StorageScpService>(_settings.LocalPort);
    }

    public void Stop()
    {
        _server?.Stop();
        _server?.Dispose();
        _server = null;
    }
}

public class StorageScpService : DicomService, IDicomServiceProvider, IDicomCStoreProvider
{
    public static string StorageFolder { get; set; } = "Storage";
    private readonly ILogger _logger;

    public StorageScpService(
        INetworkStream stream,
        Encoding fallbackEncoding,
        ILogger logger,
        DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies)
    {
        _logger = logger;
    }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        var accepted = 0;
        var rejected = 0;

        foreach (var pc in association.PresentationContexts)
        {
            if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
            {
                pc.SetResult(DicomPresentationContextResult.Accept);
                accepted++;
            }
            else
            {
                rejected++;
            }
        }

        _logger.LogInformation(
            "C-STORE association request: calling={CallingAE}, called={CalledAE}, acceptedContexts={Accepted}, rejectedContexts={Rejected}",
            association.CallingAE,
            association.CalledAE,
            accepted,
            rejected);

        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync()
        => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        => _logger.LogWarning("Abort: {source} {reason}", source, reason);

    public void OnConnectionClosed(Exception exception)
        => _logger.LogInformation("Connection closed: {ex}", exception?.Message);

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        var sop = request.SOPInstanceUID?.UID ?? Guid.NewGuid().ToString("N");
        var studyUid = request.Dataset?.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") ?? "";
        var targetFolder = string.IsNullOrWhiteSpace(studyUid)
            ? StorageFolder
            : Path.Combine(StorageFolder, SanitizePathPart(studyUid));
        Directory.CreateDirectory(targetFolder);

        var fileName = Path.Combine(targetFolder, sop + ".dcm");

        await request.File.SaveAsync(fileName);
        _logger.LogInformation(
            "C-STORE saved: sop={Sop}, study={StudyUid}, file={File}",
            sop,
            studyUid,
            fileName);

        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        _logger.LogError(e, "C-STORE exception tempFile={temp}", tempFileName);
        return Task.CompletedTask;
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
