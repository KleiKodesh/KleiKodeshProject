using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KezayitLib.Diagnostics
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
            CollectSqliteInterop(d);
            CollectAssemblyPaths(d);

            return d;
        }

        // ── process ───────────────────────────────────────────────────────────────

        private static void CollectProcess(Dictionary<string, string> d)
        {
            d["process.bitness"]     = IntPtr.Size == 8 ? "64-bit" : "32-bit";
            d["process.is64bit"]     = Environment.Is64BitProcess.ToString();
            d["process.executable"]  = SafeGet(() => System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        }

        // ── OS ────────────────────────────────────────────────────────────────────

        private static void CollectOs(Dictionary<string, string> d)
        {
            d["os.is64bit"]  = Environment.Is64BitOperatingSystem.ToString();
            d["os.bitness"]  = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            d["os.version"]  = Environment.OSVersion.VersionString;
            d["os.platform"] = Environment.OSVersion.Platform.ToString();
        }

        // ── .NET runtime ──────────────────────────────────────────────────────────

        private static void CollectDotNet(Dictionary<string, string> d)
        {
            d["dotnet.version"]     = Environment.Version.ToString();
            d["dotnet.clrVersion"]  = RuntimeEnvironment.GetSystemVersion();
            d["dotnet.runtimeDir"]  = SafeGet(() => RuntimeEnvironment.GetRuntimeDirectory());
        }

        // ── Office / Word installation ────────────────────────────────────────────

        private static void CollectOffice(Dictionary<string, string> d)
        {
            // Word bitness is stored in the registry under the ClickToRun or MSI install key.
            // We check both the 64-bit and 32-bit registry hives to cover all install types.

            string wordVersion  = null;
            string wordBitness  = null;
            string wordPath     = null;
            string installType  = null;

            // ── ClickToRun (Microsoft 365 / Office 2019+) ────────────────────────
            // HKLM\SOFTWARE\Microsoft\Office\ClickToRun\Configuration
            wordBitness = SafeGet(() =>
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
                {
                    if (key == null) return null;
                    installType = "ClickToRun";
                    wordVersion = key.GetValue("VersionToReport") as string;
                    wordPath    = key.GetValue("InstallationPath") as string;
                    return key.GetValue("Platform") as string; // "x86" or "x64"
                }
            });

            // ── MSI install (Office 2016 and earlier) ────────────────────────────
            // Check both hives: 64-bit Office writes to the native hive,
            // 32-bit Office on 64-bit Windows writes to Wow6432Node.
            if (wordBitness == null)
            {
                wordBitness = SafeGet(() =>
                {
                    // 64-bit Office on 64-bit OS
                    using (var key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Office\16.0\Word\InstallRoot"))
                    {
                        if (key != null)
                        {
                            installType = "MSI-64";
                            wordPath    = key.GetValue("Path") as string;
                            return "x64";
                        }
                    }
                    return null;
                });
            }

            if (wordBitness == null)
            {
                wordBitness = SafeGet(() =>
                {
                    // 32-bit Office on 64-bit OS (Wow6432Node)
                    using (var key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Wow6432Node\Microsoft\Office\16.0\Word\InstallRoot"))
                    {
                        if (key != null)
                        {
                            installType = "MSI-32";
                            wordPath    = key.GetValue("Path") as string;
                            return "x86";
                        }
                    }
                    return null;
                });
            }

            // ── Office 2013 (version 15.0) ───────────────────────────────────────
            if (wordBitness == null)
            {
                wordBitness = SafeGet(() =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Office\15.0\Word\InstallRoot"))
                    {
                        if (key != null) { installType = "MSI-64-2013"; wordPath = key.GetValue("Path") as string; return "x64"; }
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Wow6432Node\Microsoft\Office\15.0\Word\InstallRoot"))
                    {
                        if (key != null) { installType = "MSI-32-2013"; wordPath = key.GetValue("Path") as string; return "x86"; }
                    }
                    return null;
                });
            }

            d["office.wordBitness"]  = wordBitness  ?? "not found";
            d["office.wordVersion"]  = wordVersion  ?? "not found";
            d["office.wordPath"]     = wordPath     ?? "not found";
            d["office.installType"]  = installType  ?? "not found";

            // Verify the actual WINWORD.EXE PE header bitness as ground truth
            if (!string.IsNullOrEmpty(wordPath))
            {
                string winword = Path.Combine(wordPath.TrimEnd('\\', '/'), "WINWORD.EXE");
                if (!File.Exists(winword))
                    winword = Path.Combine(wordPath.TrimEnd('\\', '/'), "root", "Office16", "WINWORD.EXE");
                if (File.Exists(winword))
                {
                    string peBitness = SafeGet(() => ReadPeMachine(winword));
                    d["office.winwordExe"]        = winword;
                    d["office.winwordPeBitness"]  = peBitness ?? "unreadable";
                }
                else
                {
                    d["office.winwordExe"] = "not found at " + wordPath;
                }
            }
        }

        // ── SQLite.Interop.dll ────────────────────────────────────────────────────

        private static void CollectSqliteInterop(Dictionary<string, string> d)
        {
            // The stub SQLite package copies both x86\ and x64\ subfolders next to the
            // executable. We check both, report presence, file size, and PE machine type.

            string baseDir = SafeGet(() => Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location));

            if (string.IsNullOrEmpty(baseDir))
            {
                d["sqlite.baseDir"] = "unknown";
                return;
            }

            d["sqlite.baseDir"] = baseDir;

            foreach (string arch in new[] { "x86", "x64" })
            {
                string path = Path.Combine(baseDir, arch, "SQLite.Interop.dll");
                string prefix = "sqlite.interop." + arch;

                if (!File.Exists(path))
                {
                    d[prefix + ".present"]  = "false";
                    d[prefix + ".path"]     = path;
                    continue;
                }

                d[prefix + ".present"]  = "true";
                d[prefix + ".path"]     = path;
                d[prefix + ".size"]     = SafeGet(() => new FileInfo(path).Length.ToString()) ?? "?";
                d[prefix + ".peMachine"] = SafeGet(() => ReadPeMachine(path)) ?? "unreadable";
            }

            // Also check if SQLite.Interop.dll exists flat next to the exe (wrong layout)
            string flat = Path.Combine(baseDir, "SQLite.Interop.dll");
            d["sqlite.interop.flat.present"] = File.Exists(flat).ToString();
            if (File.Exists(flat))
            {
                d["sqlite.interop.flat.peMachine"] = SafeGet(() => ReadPeMachine(flat)) ?? "unreadable";
            }
        }

        // ── assembly load paths ───────────────────────────────────────────────────

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
