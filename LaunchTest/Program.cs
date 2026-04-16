// Test strategy: launch target process immediately from ProcessExit,
// but the target hides and waits for the parent to fully die before showing.

using System.Diagnostics;

var logPath = Path.Combine(Path.GetTempPath(), "LaunchTest.log");
File.WriteAllText(logPath, $"=== LaunchTest started {DateTime.Now} ===\r\n");
void Log(string msg) { try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n"); } catch { } }

var proofFile  = Path.Combine(Path.GetTempPath(), "LaunchTest_proof.txt");
var targetExe  = Path.Combine(AppContext.BaseDirectory, "LaunchTarget.exe");
if (File.Exists(proofFile)) File.Delete(proofFile);

Log($"Target exe: {targetExe}");
Log($"Proof file: {proofFile}");

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Log("=== ProcessExit fired ===");
    try
    {
        // Launch immediately, pass our PID so target can wait for us to die
        var myPid = Environment.ProcessId;
        var p = Process.Start(new ProcessStartInfo
        {
            FileName        = targetExe,
            Arguments       = $"--wait-for-pid {myPid} --proof \"{proofFile}\"",
            UseShellExecute = false,
            CreateNoWindow  = false,   // will show window after parent dies
        });
        Log($"Target launched pid={p?.Id}");
    }
    catch (Exception ex) { Log($"FAILED: {ex.Message}"); }
    Log("=== ProcessExit handler done ===");
};

Log("Working for 1s...");
Thread.Sleep(1000);
Log("Exiting...");
