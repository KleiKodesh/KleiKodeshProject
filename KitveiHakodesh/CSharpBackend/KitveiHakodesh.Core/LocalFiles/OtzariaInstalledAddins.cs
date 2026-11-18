using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KitveiHakodesh.Core.Common;
using MessagePack;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>One installed Otzaria addin: its manifest name and entry-point file.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class OtzariaInstalledAddin
{
    public string Name { get; set; } = "";
    /// <summary>Full path of the entry-point file (manifest "entrypoint", default index.html).</summary>
    public string EntryPath { get; set; } = "";
}

/// <summary>
/// The addins Otzaria has installed, read straight from its plugins folder
/// (%AppData%\otzaria\plugins\installed\{id}\{release}\manifest.json) — instant, unlike the
/// file index, which may still be building. Read-only: these are Otzaria's files, not ours.
/// When an addin has several release folders, the newest one is the installed one.
/// </summary>
public static class OtzariaInstalledAddins
{
    public static List<OtzariaInstalledAddin> List()
    {
        var result = new List<OtzariaInstalledAddin>();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "otzaria", "plugins", "installed");
        if (!Directory.Exists(root)) return result;

        foreach (string addinDir in Directory.EnumerateDirectories(root))
        {
            // The manifest is in the addin folder itself (hand-dropped) or in one of its release folders.
            var candidates = new[] { addinDir }
                .Concat(SafeSubdirectories(addinDir).OrderByDescending(Directory.GetLastWriteTimeUtc))
                .Where(d => File.Exists(Path.Combine(d, "manifest.json")));
            foreach (string folder in candidates)
            {
                var addin = ReadManifest(folder);
                if (addin is null) continue;
                result.Add(addin);
                break;
            }
        }
        return result;
    }

    private static IEnumerable<string> SafeSubdirectories(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    /// <summary>The addin a manifest describes; null when it has no name or its entry point is missing.</summary>
    private static OtzariaInstalledAddin? ReadManifest(string folder)
    {
        try
        {
            using var doc = JsonDocument.Parse(TextEncodingDetector.ReadAllText(Path.Combine(folder, "manifest.json")));
            var rootEl = doc.RootElement;
            string name = StringField(rootEl, "name");
            if (name.Length == 0) return null;
            // Otzaria emits "entrypoint"; "main" is accepted as the common JS convention.
            string entry = StringField(rootEl, "entrypoint");
            if (entry.Length == 0) entry = StringField(rootEl, "main");
            if (entry.Length == 0) entry = "index.html";
            string entryPath = Path.GetFullPath(Path.Combine(folder, entry.Replace('/', '\\').TrimStart('\\')));
            return File.Exists(entryPath) ? new OtzariaInstalledAddin { Name = name.Trim(), EntryPath = entryPath } : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string StringField(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
