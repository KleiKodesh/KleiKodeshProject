// Simulates the WPF installer launched from Word's shutdown event.
// Waits for the parent process (Word) to fully exit, then "shows UI" (writes proof file).

using System.Diagnostics;

var cmdArgs = Environment.GetCommandLineArgs();
int waitPid  = 0;
string proof = "";

for (int i = 1; i < cmdArgs.Length; i++)
{
    if (cmdArgs[i] == "--wait-for-pid" && i + 1 < cmdArgs.Length) waitPid = int.Parse(cmdArgs[i + 1]);
    if (cmdArgs[i] == "--proof"        && i + 1 < cmdArgs.Length) proof   = cmdArgs[i + 1];
}

var logPath = Path.Combine(Path.GetTempPath(), "LaunchTest.log");
void Log(string msg) { try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} [TARGET] {msg}\r\n"); } catch { } }

Log($"Started. Waiting for pid={waitPid} to exit...");

// Wait for parent to fully die
if (waitPid > 0)
{
    try
    {
        var parent = Process.GetProcessById(waitPid);
        parent.WaitForExit();
        Log("Parent exited.");
    }
    catch { Log("Parent already gone."); }
}

// Small extra delay for file handle release
Thread.Sleep(1000);

// "Show UI" — write proof file
Log($"Writing proof to '{proof}'");
File.WriteAllText(proof, $"success at {DateTime.Now}");
Log("Done.");
