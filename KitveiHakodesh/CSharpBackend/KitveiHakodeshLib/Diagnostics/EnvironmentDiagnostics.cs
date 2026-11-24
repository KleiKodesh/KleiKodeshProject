using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using KitveiHakodeshLib.Common;

namespace KitveiHakodeshLib.Diagnostics
{
    /// <summary>
    /// Collects environment facts relevant to the SQLite bitness mismatch error
    /// (HRESULT 0x8007000B). Designed to be called from the WebView bridge so the
    /// frontend can display a structured diagnostic report to the user or send it
    /// as a bug report.
    /// </summary>
    public static class EnvironmentDiagnostics
    {
        // ── PE header constants ───────────────────────────────────────────────────
        private const ushort IMAGE_FILE_MACHINE_I386  = 0x014C;
        private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
        private const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;

        // ── public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a flat dictionary of diagnostic key/value pairs.
        /// All values are strings so they serialise cleanly to JSON.
        /// </summary>
        public static Dictionary<string, string> Collect()
        {
            var d = new Dictionary<string, string>();

            CollectProcess(d);
            CollectOs(d);
            CollectDotNet(d);
            CollectOffice(d);
            CollectAssemblyPaths(d);

            return d;
        }

        // ── process ───────────────────────────────────────────────────────────────

        // ── process, OS, .NET runtime, Word ───────────────────────────────────────
        //
        // All four are the probes in Common/ (moved here from Core). They used to be re-implemented, which
        // is how two copies of "which bitness is Word?" end up disagreeing. Core answers each
        // one as TYPED data; shaping it into this flat string dictionary is the report's job,
        // and that is what stays on this side.

        private static void CollectProcess(Dictionary<string, string> d)
        {
            d["process.bitness"]    = ProcessBitnessProbe.Bitness;
            d["process.is64bit"]    = ProcessBitnessProbe.Is64Bit.ToString();
            d["process.executable"] = SafeGet(() =>
                System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) ?? "unknown";
        }

        private static void CollectOs(Dictionary<string, string> d)
        {
            d["os.is64bit"]  = OperatingSystemProbe.Is64Bit.ToString();
            d["os.bitness"]  = OperatingSystemProbe.Bitness;
            d["os.version"]  = OperatingSystemProbe.Version;
            d["os.platform"] = OperatingSystemProbe.Platform;
        }

        private static void CollectDotNet(Dictionary<string, string> d)
        {
            d["dotnet.version"]     = DotNetRuntimeProbe.Version;
            d["dotnet.description"] = DotNetRuntimeProbe.Description;
        }

        private static void CollectOffice(Dictionary<string, string> d)
        {
            var word = WordInstallProbe.Detect();

            // "not found" rather than an empty string: the report is read by a person, and a
            // blank cell does not say whether the probe ran.
            d["office.installType"] = word.Kind?.ToString() ?? "not found";
            d["office.wordVersion"] = word.Version ?? "not found";
            d["office.wordBitness"] = word.Bitness ?? "not found";
            d["office.wordPath"]    = word.InstallPath ?? "not found";

            // The registry says what was INSTALLED; the PE header says what is actually on disk.
            // They disagree on a repaired or hand-copied install, which is exactly the case this
            // report exists to show.
            string winword = WordInstallProbe.FindWinword(word.InstallPath);
            if (winword != null)
            {
                d["office.winwordExe"]       = winword;
                d["office.winwordPeBitness"] = WordInstallProbe.WinwordPeBitness(winword) ?? "unreadable";
            }
            else if (word.InstallPath != null)
            {
                d["office.winwordExe"] = "not found at " + word.InstallPath;
            }
        }

        // The SQLite.Interop.dll probes that used to live here are GONE, not moved. They
        // diagnosed System.Data.SQLite's native loader — the provider this app no longer uses.
        // Everything here now reaches SQLite through Microsoft.Data.Sqlite, which has no
        // per-architecture interop DLL to go missing, so there is nothing left for them to find.


        private static void CollectAssemblyPaths(Dictionary<string, string> d)
        {
            d["assembly.executingLocation"] = SafeGet(() =>
                Assembly.GetExecutingAssembly().Location) ?? "unknown";

            d["assembly.entryLocation"] = SafeGet(() =>
                Assembly.GetEntryAssembly()?.Location) ?? "unknown";

            d["assembly.appDomainBase"] = SafeGet(() =>
                AppDomain.CurrentDomain.BaseDirectory) ?? "unknown";
        }

        // ── PE header reader ──────────────────────────────────────────────────────

        /// <summary>
        /// Reads the PE machine type from a DLL/EXE without loading it.
        /// Returns "x86", "x64", "ARM64", or "unknown (0x{hex})".
        /// </summary>
        private static string ReadPeMachine(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                // DOS header: magic "MZ" at offset 0, PE offset at 0x3C
                if (br.ReadUInt16() != 0x5A4D) // "MZ"
                    return "not a PE file";

                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();

                fs.Seek(peOffset, SeekOrigin.Begin);
                uint peSig = br.ReadUInt32();
                if (peSig != 0x00004550) // "PE\0\0"
                    return "invalid PE signature";

                ushort machine = br.ReadUInt16();
                switch (machine)
                {
                    case IMAGE_FILE_MACHINE_I386:  return "x86";
                    case IMAGE_FILE_MACHINE_AMD64: return "x64";
                    case IMAGE_FILE_MACHINE_ARM64: return "ARM64";
                    default: return "unknown (0x" + machine.ToString("X4") + ")";
                }
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private static string SafeGet(Func<string> fn)
        {
            try { return fn(); }
            catch (Exception ex) { return "error: " + ex.Message; }
        }
    }
}
