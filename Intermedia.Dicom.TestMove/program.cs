using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

class Program
{
    static async Task Main(string[] args)
    {
        // ✅ fo-dicom v5 için DI setup (zorunlu)
        var services = new ServiceCollection();
        services.AddFellowOakDicom();
        var sp = services.BuildServiceProvider();
        DicomSetupBuilder.UseServiceProvider(sp);

        string pacsIp = "127.0.0.1";
        int pacsPort = 104;

        string callingAe = "TESTCLIENT";
        string calledAe = "interMEDIAPacs";

        string storageAe = "LOCALSTORAGE";
        int storagePort = 11112;

        Directory.CreateDirectory("Storage");

        // ✅ SCP başlat (v5 doğru)
        var server = DicomServerFactory.Create<StorageSCP>(storagePort);
        Console.WriteLine($"Storage SCP çalışıyor → AE: {storageAe}, Port: {storagePort}");

        // ✅ C-FIND
        Console.WriteLine("\nC-FIND başlatılıyor...\n");
        var client = DicomClientFactory.Create(pacsIp, pacsPort, false, callingAe, calledAe);

        var findRequest = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        findRequest.Dataset.AddOrUpdate(DicomTag.PatientName, "");
        findRequest.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");

        findRequest.OnResponseReceived += (req, res) =>
        {
            if (res.Status == DicomStatus.Pending)
            {
                var studyUid = res.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
                var patientName = res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "");
                Console.WriteLine($"StudyUID: {studyUid}");
                Console.WriteLine($"Patient: {patientName}");
                Console.WriteLine("----------------------------");
            }
        };

        await client.AddRequestAsync(findRequest);
        await client.SendAsync();

        Console.Write("\nTaşımak istediğiniz StudyInstanceUID girin: ");
        var studyInstanceUid = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(studyInstanceUid))
        {
            Console.WriteLine("StudyInstanceUID boş olamaz.");
            return;
        }

        // ✅ C-MOVE (hedef: bizim SCP AE)
        Console.WriteLine("C-MOVE gönderiliyor...");
        var moveClient = DicomClientFactory.Create(pacsIp, pacsPort, false, callingAe, calledAe);
        var moveRequest = new DicomCMoveRequest(storageAe, studyInstanceUid);

        moveRequest.OnResponseReceived += (req, res) =>
        {
            Console.WriteLine($"C-MOVE Status: {res.Status}");
        };

        await moveClient.AddRequestAsync(moveRequest);
        await moveClient.SendAsync();

        Console.WriteLine("Bitti. Storage klasörünü kontrol et.");
        Console.ReadKey();

        server.Dispose();
    }
}