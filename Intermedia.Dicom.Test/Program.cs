using System;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;        // DicomCFindRequest, DicomCMoveRequest buradan
using FellowOakDicom.Network.Client; // DicomClientFactory

class Program
{
    static async Task Main()
    {
        string host = "127.0.0.1";
        int port = 104;
        string calledAe = "interMEDIAPacs"; // PACS AE Title
        string callingAe = "TESTCLIENT";     // Local AE Title

        var client = DicomClientFactory.Create(host, port, false, callingAe, calledAe);

        // C-FIND Study query
        var findRequest = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        findRequest.Dataset.Add(DicomTag.PatientID, "");       // boş veya filtreli
        findRequest.Dataset.Add(DicomTag.PatientName, "");
        findRequest.Dataset.Add(DicomTag.StudyInstanceUID, "");

        findRequest.OnResponseReceived += (req, res) =>
        {
            if (res.Status == DicomStatus.Pending && res.Dataset != null)
            {
                Console.WriteLine($"Study: {res.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "")}, Patient: {res.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "")}");
            }
        };

        await client.AddRequestAsync(findRequest);
        await client.SendAsync();

        // Örnek C-MOVE
        string studyInstanceUid = "1.3.12.2.1107.5.2.60.209540.30000025062707582728100000005";
        var moveRequest = new DicomCMoveRequest(calledAe, studyInstanceUid);

        moveRequest.OnResponseReceived += (req, res) =>
        {
            Console.WriteLine($"C-MOVE Status={res.Status}, Remaining={res.Remaining}");
        };

        await client.AddRequestAsync(moveRequest);
        await client.SendAsync();

        Console.WriteLine("Done.");
    }
}