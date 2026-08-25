using System;
using System.IO;
using Microsoft.Win32;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Where is Microsoft Word installed, which edition is it, and is it 32- or 64-bit?
    ///
    /// This is the question behind almost every add-in that loads but does not work. Word's
    /// bitness decides the add-in's, and a 32-bit native dependency in a 64-bit Word fails at
    /// load with a message that names neither.
    ///
    /// Four install layouts, checked newest first, because they store the same facts in four
    /// different places:
    ///
    ///   ClickToRun    Microsoft 365 and Office 2019+, one key that states the platform outright
    ///   MSI 16.0      Office 2016; 64-bit writes the native hive, 32-bit writes Wow6432Node,
    ///                 and WHICH hive holds the key IS the bitness
    ///   MSI 15.0      Office 2013, the same trick one version back
    ///
    /// The registry is what Office claims. <see cref="WinwordPeBitness"/> reads the machine
    /// type out of WINWORD.EXE's PE header, which is what the loader will actually enforce —
    /// worth having both when they disagree.
    /// </summary>
    public static class WordInstallProbe
    {
        private const string ClickToRunKey = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";

        // Machine types from the PE specification.
        private const ushort MachineI386 = 0x014c;
        private const ushort MachineAmd64 = 0x8664;
        private const ushort MachineArm64 = 0xAA64;

        /// <summary>How Word was installed, or null when no install was found.</summary>
        public enum InstallKind
        {
            ClickToRun,
            Msi64,
            Msi32,
            Msi64Office2013,
            Msi32Office2013,
        }

        /// <summary>What the registry says about the installed Word. Every field may be null:
        /// the layouts do not all record the same things, and "not recorded" is different from
        /// "not installed" — which is <see cref="Found"/>.</summary>
        public sealed class WordInstall
        {
            public InstallKind? Kind { get; internal set; }

            /// <summary>The version Office reports. Only ClickToRun records one.</summary>
            public string? Version { get; internal set; }

            /// <summary>"x86" or "x64" per the registry. For MSI installs this is inferred from
            /// WHICH hive held the key, which is reliable — a 32-bit Office cannot write the
            /// native hive.</summary>
            public string? Bitness { get; internal set; }

            /// <summary>The install root, from which WINWORD.EXE is located.</summary>
            public string? InstallPath { get; internal set; }

            public bool Found => Kind != null;
        }

        /// <summary>
        /// Finds the installed Word, or returns an empty result when there is none.
        /// Reads HKLM only — this is a machine-wide install, not a user preference.
        /// </summary>
        public static WordInstall Detect()
        {
            var install = new WordInstall();

            if (TryClickToRun(install)) return install;
            if (TryMsi(install, @"SOFTWARE\Microsoft\Office\16.0\Word\InstallRoot", InstallKind.Msi64, "x64")) return install;
            if (TryMsi(install, @"SOFTWARE\Wow6432Node\Microsoft\Office\16.0\Word\InstallRoot", InstallKind.Msi32, "x86")) return install;
            if (TryMsi(install, @"SOFTWARE\Microsoft\Office\15.0\Word\InstallRoot", InstallKind.Msi64Office2013, "x64")) return install;
            if (TryMsi(install, @"SOFTWARE\Wow6432Node\Microsoft\Office\15.0\Word\InstallRoot", InstallKind.Msi32Office2013, "x86")) return install;

            return install;
        }

        private static bool TryClickToRun(WordInstall install)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ClickToRunKey);
                if (key == null) return false;

                install.Kind = InstallKind.ClickToRun;
                install.Version = key.GetValue("VersionToReport") as string;
                install.InstallPath = key.GetValue("InstallationPath") as string;
                install.Bitness = key.GetValue("Platform") as string;   // "x86" or "x64"
                return true;
            }
            catch (Exception)
            {
                return false;   // a hive we cannot read is the same as one that is not there
            }
        }

        private static bool TryMsi(WordInstall install, string keyPath, InstallKind kind, string bitness)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key == null) return false;

                install.Kind = kind;
                install.InstallPath = key.GetValue("Path") as string;
                install.Bitness = bitness;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Full path to WINWORD.EXE for a given install root, or null if it is not there.
        /// Two layouts: MSI puts it directly under the recorded path, ClickToRun nests it under
        /// root\Office16.
        /// </summary>
        public static string? FindWinword(string? installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath)) return null;

            try
            {
                string root = installPath!.TrimEnd('\\', '/');

                string direct = Path.Combine(root, "WINWORD.EXE");
                if (File.Exists(direct)) return direct;

                string clickToRun = Path.Combine(root, "root", "Office16", "WINWORD.EXE");
                if (File.Exists(clickToRun)) return clickToRun;

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The bitness the loader will actually enforce, read from WINWORD.EXE's PE header —
        /// "x86", "x64", "ARM64", or null when the file cannot be read.
        ///
        /// This is ground truth where the registry is a claim, and it costs a few bytes: the
        /// header is read without loading the image.
        /// </summary>
        public static string? WinwordPeBitness(string? winwordPath)
        {
            if (string.IsNullOrWhiteSpace(winwordPath)) return null;

            try
            {
                using var file = new FileStream(
                    winwordPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(file);

                if (reader.ReadUInt16() != 0x5A4D) return null;      // "MZ"

                file.Seek(0x3C, SeekOrigin.Begin);                    // e_lfanew
                int peOffset = reader.ReadInt32();

                file.Seek(peOffset, SeekOrigin.Begin);
                if (reader.ReadUInt32() != 0x00004550) return null;   // "PE\0\0"

                ushort machine = reader.ReadUInt16();
                switch (machine)
                {
                    case MachineI386: return "x86";
                    case MachineAmd64: return "x64";
                    case MachineArm64: return "ARM64";
                    default: return "unknown (0x" + machine.ToString("X4") + ")";
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
