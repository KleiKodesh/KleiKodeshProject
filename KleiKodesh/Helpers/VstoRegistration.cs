using Microsoft.Win32;
using System;
using System.IO;

namespace KleiKodesh.Helpers
{
    public static class VstoRegistration
    {
        private const string AppName = "כלי קודש";
        private const string AddinRegistryPath = @"Software\Microsoft\Office\Word\Addins\KleiKodesh";
        private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh";
        
        public static void RegisterAddin(string installPath, string vstoPath)
        {
            if (!File.Exists(vstoPath))
            {
                throw new FileNotFoundException($"VSTO file not found: {vstoPath}");
            }
            
            // Register for both 32-bit and 64-bit Office
            RegisterForArchitecture(RegistryView.Registry64, installPath, vstoPath);
            RegisterForArchitecture(RegistryView.Registry32, installPath, vstoPath);
        }
        
        private static void RegisterForArchitecture(RegistryView view, string installPath, string vstoPath)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            {
                // Register VSTO Add-in
                using (var addinKey = baseKey.CreateSubKey(AddinRegistryPath))
                {
                    addinKey.SetValue("FriendlyName", AppName);
                    addinKey.SetValue("Manifest", $"file:///{vstoPath.Replace('\\', '/')}|vstolocal");
                    addinKey.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                }
                
                // Register for Add/Remove Programs (only once for 64-bit)
                if (view == RegistryView.Registry64)
                {
                    using (var uninstallKey = baseKey.CreateSubKey(UninstallRegistryPath))
                    {
                        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                        uninstallKey.SetValue("DisplayName", $"{AppName} v{version}");
                        uninstallKey.SetValue("UninstallString", Path.Combine(installPath, "Update.exe") + " --uninstall");
                        uninstallKey.SetValue("InstallLocation", installPath);
                        uninstallKey.SetValue("DisplayVersion", version);
                        uninstallKey.SetValue("Publisher", "Your Company Name");
                        uninstallKey.SetValue("DisplayIcon", Path.Combine(installPath, "KleiKodesh.exe"));
                    }
                }
            }
        }
        
        public static void UnregisterAddin()
        {
            UnregisterForArchitecture(RegistryView.Registry64);
            UnregisterForArchitecture(RegistryView.Registry32);
        }
        
        private static void UnregisterForArchitecture(RegistryView view)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            {
                try
                {
                    baseKey.DeleteSubKeyTree(AddinRegistryPath, false);
                }
                catch { }
                
                if (view == RegistryView.Registry64)
                {
                    try
                    {
                        baseKey.DeleteSubKeyTree(UninstallRegistryPath, false);
                    }
                    catch { }
                }
            }
            
            // Also try to remove from current user registry
            try
            {
                using (var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                {
                    currentUser.DeleteSubKeyTree(AddinRegistryPath, false);
                }
            }
            catch { }
        }
    }
}