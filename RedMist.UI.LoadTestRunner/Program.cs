using System.Diagnostics;

namespace RedMist.UI.LoadTestRunner;

internal class Program
{
    const string EXE_PATH = "C:\\Code\\redmist-timing-ui\\RedMist.Timing.UI.Desktop\\bin\\Debug\\net9.0\\RedMist.Timing.UI.Desktop.exe";
    const int INSTANCES = 100;
    const int EVENT_ID = 36;

    static void Main()
    {
        Console.WriteLine($"Starting {INSTANCES} instances.");

        var processIds = new List<int>();
        for (int i = 0; i < INSTANCES; i++)
        {
            var si = new ProcessStartInfo(EXE_PATH, EVENT_ID.ToString());
            var p = Process.Start(si);
            processIds.Add(p!.Id);
            Console.WriteLine($"Started {p.Id}");
        }

        Console.WriteLine("Press enter to terminate instances");
        Console.ReadLine();

        foreach (var processId in processIds)
        {
            try
            {
                Process.GetProcessById(processId).Kill();
            }
            catch { }
        }
    }
}
