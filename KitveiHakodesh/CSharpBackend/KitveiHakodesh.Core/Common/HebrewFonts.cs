using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Enumerates system font families that can render Hebrew, through DirectWrite.
    ///
    /// It also reports which of those families draw the TE'AMIM (cantillation marks), read from
    /// the same cmap walk. Hebrew coverage and te'amim coverage are separate questions: most
    /// Hebrew fonts draw no marks at all, so the te'amim picker needs the narrower list.
    ///
    /// THE ONE COPY. This replaces three identical implementations — KitveiHakodeshLib's
    /// FontsProvider, the service's HebrewFontsProvider and WpfLib's FontsProvider — which
    /// differed only in the factory import and in which subset of the surface each exposed.
    /// This is the union: every consumer (the font dropdown, WpfLib's FontsHelper, RegexFindLib's
    /// picker, the service) reads it, and an enumeration or cmap bug now gets fixed once.
    ///
    /// WHY DIRECTWRITE AND NOT WPF (verified 2026-08-20): WPF's Fonts.SystemFontFamilies is a
    /// process-lifetime snapshot. After a per-user font install plus AddFontResource plus a
    /// WM_FONTCHANGE broadcast, InstalledFontsChanged fired and re-enumeration still returned the
    /// old set — a long-running app never saw a new font until restart. DirectWrite's
    /// checkForUpdates re-scan refreshes in-process, verified live with a forged family.
    /// System.Drawing's InstalledFontCollection has the same staleness problem AND reports GDI
    /// families rather than the ones WPF renders by name.
    ///
    /// THE HEBREW TEST reads each face's own 'cmap' table for U+05D0, which is what WPF's
    /// CharacterToGlyphMap effectively does. Measured against the WPF list this is a strict
    /// superset: WPF's per-typeface enumeration misses the WindowsApps Cascadia faces, which DO
    /// map the full Hebrew alphabet. Names come from the family's localized-name table, preferring
    /// en-us, so spelling matches WPF's FontFamily.Source.
    ///
    /// Every call goes through delegate* unmanaged[Stdcall] function pointers. The explicit single
    /// [Stdcall] specifier matters: it compiles to the flat calling-convention signature encoding
    /// rather than the modopt form, and only the flat one works on .NET Framework.
    /// Slot indices are fixed by each interface's layout and must not be reordered; every COM
    /// interface begins with the three IUnknown slots (QueryInterface, AddRef, Release).
    /// </summary>
    public static partial class HebrewFonts
    {
        // The ONLY per-leg difference in this file. LibraryImport is a source generator and
        // needs a partial method (hence the partial class); net48 has neither and uses DllImport.
#if NET7_0_OR_GREATER
        [LibraryImport("dwrite.dll")]
        private static partial int DWriteCreateFactory(uint factoryType, in Guid iid, out nint factory);
#else
        [DllImport("dwrite.dll")]
        private static extern int DWriteCreateFactory(uint factoryType, in Guid iid, out nint factory);
#endif

        private const uint DWRITE_FACTORY_TYPE_SHARED = 0;
        private static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

        // The Hebrew probe character — א (U+05D0).
        private const uint HebrewAlef = 0x05D0;

        // The te'amim probe character - ETNAHTA (U+0591), first of the cantillation block.
        // A font either draws the te'amim or it does not: measured over all 142 Hebrew-capable
        // font files on a dev box, every family mapping this one maps essentially the whole
        // U+0591-05AF range (30 or 31 of the 31 marks), and every family missing it maps at
        // most a single mark. U+05AB is the one to
        // AVOID probing - David CLM and Miriam CLM carry that glyph ALONE, so testing it
        // reports six te'amim-less families as capable.
        private const uint TeamimEtnahta = 0x0591;

        // IDWriteFactory: 0-2 = IUnknown, then GetSystemFontCollection at slot 3.
        private const int SlotGetSystemFontCollection = 3;

        // IDWriteFontCollection: 3 = GetFontFamilyCount, 4 = GetFontFamily, 5 = FindFamilyName.
        private const int SlotGetFontFamilyCount = 3;
        private const int SlotGetFontFamily = 4;
        private const int SlotFindFamilyName = 5;

        // IDWriteFontFamily inherits IDWriteFontList (3 = GetFontCollection, 4 = GetFontCount,
        // 5 = GetFont), then adds GetFamilyNames at slot 6.
        private const int SlotGetFontCount = 4;
        private const int SlotGetFont = 5;
        private const int SlotGetFamilyNames = 6;

        // IDWriteFont: 3 = GetFontFamily, 4 = GetWeight … 9 = GetInformationalStrings,
        // 10 = GetSimulations, 11 = GetMetrics, 12 = HasCharacter, 13 = CreateFontFace.
        private const int SlotCreateFontFace = 13;

        // IDWriteFontFace: 3 = GetType, 4 = GetFiles, 5 = GetIndex, 6 = GetSimulations,
        // 7 = IsSymbolFont, 8 = GetMetrics, 9 = GetGlyphCount, 10 = GetDesignGlyphMetrics,
        // 11 = GetGlyphIndices, 12 = TryGetFontTable, 13 = ReleaseFontTable.
        private const int SlotTryGetFontTable = 12;
        private const int SlotReleaseFontTable = 13;

        // 'cmap' as a big-endian OpenType tag, byte-reversed for the little-endian DWRITE_FONT_TABLE_TAG.
        private const uint TagCmap = 'c' | ('m' << 8) | ('a' << 16) | ('p' << 24);

        // IDWriteLocalizedStrings: 3 = GetCount, 4 = FindLocaleName, 5 = GetLocaleNameLength,
        // 6 = GetLocaleName, 7 = GetStringLength, 8 = GetString.
        private const int SlotFindLocaleName = 4;
        private const int SlotGetStringLength = 7;
        private const int SlotGetString = 8;

        private const int SlotRelease = 2;

        private static unsafe nint Vtbl(nint obj, int slot) => (*(nint**)obj)[slot];

        private static unsafe void Release(nint obj)
        {
            if (obj == 0) return;
            ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(obj, SlotRelease))(obj);
        }

        /// <summary>One system font family: the name to bind to FontFamily, whether it can render
        /// Hebrew, and whether it draws the te'amim.</summary>
        public sealed class FontFamilyInfo
        {
            public string Name { get; }
            public bool HasHebrew { get; }
            /// <summary>Whether the family also draws the cantillation marks. A separate question
            /// from HasHebrew: most Hebrew families draw no marks at all.</summary>
            public bool HasTeamim { get; }

            public FontFamilyInfo(string name, bool hasHebrew, bool hasTeamim)
            {
                Name = name;
                HasHebrew = hasHebrew;
                HasTeamim = hasTeamim;
            }

            public override string ToString() => Name;
        }

        // Deliberately stateless: the app is long-running and fonts can be installed or
        // removed under it, so every call enumerates fresh (checkForUpdates below re-scans) —
        // no cache to hold memory or go stale. The picker shows a loading row for the ~1s an
        // enumeration takes, and it only runs when someone opens the font dropdown.
        /// <summary>Names of every system font family that has a glyph for א, sorted alphabetically.
        /// Returns an empty array if DirectWrite is unavailable — the caller falls back to the
        /// frontend's canvas probe. Never throws.</summary>
        public static string[] GetHebrewFonts()
        {
            // Sorted here rather than relying on GetFontFamilies' order: filtering to the Hebrew
            // families happens to leave that order alphabetical today, but only because Hebrew is
            // its primary sort key. Reordering the picker must not silently reorder this.
            try
            {
                return Enumerate(hebrewOnly: true)
                    .Select(f => f.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>Names of every system font family whose cmap covers the cantillation marks,
        /// sorted alphabetically - the list the te'amim font picker offers. A strict subset of
        /// GetHebrewFonts. Never throws.</summary>
        public static string[] GetTeamimFonts()
        {
            try
            {
                return Enumerate(hebrewOnly: true)
                    .Where(f => f.HasTeamim)
                    .Select(f => f.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>Every system font family, Hebrew-capable ones first and alphabetical within
        /// each group — the order a Hebrew-first font picker wants. Returns an empty array if
        /// DirectWrite is unavailable. Never throws.</summary>
        public static FontFamilyInfo[] GetFontFamilies()
        {
            try { return Enumerate(hebrewOnly: false); }
            catch { return new FontFamilyInfo[0]; }
        }

        /// <summary>True when the named family has a glyph for א. False when the name is unknown or
        /// DirectWrite is unavailable. Never throws.
        ///
        /// For ONE font whose name you already have. It still builds a factory and re-scans the
        /// system font collection, so it is not cheap in a loop — to classify many fonts call
        /// GetFontFamilies once and read HasHebrew off the results.</summary>
        public static bool HasHebrew(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return false;
            try { return ProbeFamily(familyName); }
            catch { return false; }
        }

        private static unsafe bool ProbeFamily(string familyName)
        {
            if (DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, in IID_IDWriteFactory, out nint factory) < 0
                || factory == 0)
                return false;

            nint collection = 0;
            nint family = 0;
            try
            {
                var getCollection =
                    (delegate* unmanaged[Stdcall]<nint, nint*, int, int>)Vtbl(factory, SlotGetSystemFontCollection);
                if (getCollection(factory, &collection, 1) < 0 || collection == 0) return false;

                uint index = 0;
                int exists = 0;
                var findFamily = (delegate* unmanaged[Stdcall]<nint, ushort*, uint*, int*, int>)
                    Vtbl(collection, SlotFindFamilyName);
                fixed (char* name = familyName)
                {
                    if (findFamily(collection, (ushort*)name, &index, &exists) < 0 || exists == 0) return false;
                }

                var getFamily =
                    (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtbl(collection, SlotGetFontFamily);
                if (getFamily(collection, index, &family) < 0 || family == 0) return false;

                FamilyCoverage(family, out bool hasHebrew, out _);
                return hasHebrew;
            }
            finally
            {
                Release(family);
                Release(collection);
                Release(factory);
            }
        }

        private static unsafe FontFamilyInfo[] Enumerate(bool hebrewOnly)
        {
            if (DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, in IID_IDWriteFactory, out nint factory) < 0
                || factory == 0)
                return new FontFamilyInfo[0];

            nint collection = 0;
            try
            {
                var getCollection =
                    (delegate* unmanaged[Stdcall]<nint, nint*, int, int>)Vtbl(factory, SlotGetSystemFontCollection);
                // checkForUpdates: 1 — re-scan installed fonts NOW rather than trusting a
                // collection cached before the user's font install/remove.
                if (getCollection(factory, &collection, 1) < 0 || collection == 0) return new FontFamilyInfo[0];

                uint familyCount =
                    ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(collection, SlotGetFontFamilyCount))(collection);

                var families = new List<FontFamilyInfo>((int)familyCount);
                var getFamily =
                    (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtbl(collection, SlotGetFontFamily);

                for (uint i = 0; i < familyCount; i++)
                {
                    nint family = 0;
                    if (getFamily(collection, i, &family) < 0 || family == 0) continue;
                    try
                    {
                        // Test for Hebrew first when that is all the caller wants: reading the
                        // localized name is its own set of COM round-trips, and skipping it for
                        // the non-Hebrew majority is most of this path's cost.
                        FamilyCoverage(family, out bool hasHebrew, out bool hasTeamim);
                        if (hebrewOnly && !hasHebrew) continue;

                        string? name = ReadFamilyName(family);
                        if (!string.IsNullOrWhiteSpace(name))
                            families.Add(new FontFamilyInfo(name!, hasHebrew, hasTeamim));
                    }
                    finally { Release(family); }
                }

                // Group by name: two families can share a localized name (e.g. differing only by a
                // face DirectWrite splits out) and the dropdown must not show duplicates. A name
                // counts as Hebrew-capable when ANY of the families behind it is, matching what the
                // user sees once WPF resolves the name to a typeface.
                // Hebrew first, then alphabetical — the Hebrew-first picker order.
                return families
                    .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new FontFamilyInfo(g.Key, g.Any(f => f.HasHebrew), g.Any(f => f.HasTeamim)))
                    .OrderBy(f => f.HasHebrew ? 0 : 1)
                    .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally
            {
                Release(collection);
                Release(factory);
            }
        }

        /// <summary>What this family can draw, ORed across its faces - ANY face that maps the
        /// character counts, which is what the reader gets once the name resolves to a typeface.
        /// Reads each face's OWN cmap table. The two easier-looking APIs both give WRONG answers
        /// here (verified): IDWriteFont::HasCharacter and IDWriteFontFace::GetGlyphIndices consult
        /// system font LINKING, so they report Hebrew-less coding fonts (Cascadia) as capable.</summary>
        private static unsafe void FamilyCoverage(nint family, out bool hasHebrew, out bool hasTeamim)
        {
            hasHebrew = false;
            hasTeamim = false;

            uint fontCount = ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(family, SlotGetFontCount))(family);
            var getFont = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtbl(family, SlotGetFont);

            for (uint i = 0; i < fontCount; i++)
            {
                nint font = 0;
                if (getFont(family, i, &font) < 0 || font == 0) continue;
                nint face = 0;
                try
                {
                    var createFace = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(font, SlotCreateFontFace);
                    if (createFace(font, &face) < 0 || face == 0) continue;
                    FaceCmapCoverage(face, ref hasHebrew, ref hasTeamim);
                    if (hasHebrew && hasTeamim) return;
                }
                finally
                {
                    Release(face);
                    Release(font);
                }
            }
        }

        /// <summary>ORs this face's coverage into the flags by walking its cmap ONCE and testing
        /// both probe characters per segment - the walk is the expensive part, not the comparison.
        /// Supports the two formats that matter for Unicode text: format 4 (BMP segment mapping)
        /// and format 12 (full-range groups).</summary>
        private static unsafe void FaceCmapCoverage(nint face, ref bool hasHebrew, ref bool hasTeamim)
        {
            var tryGetTable = (delegate* unmanaged[Stdcall]<nint, uint, void**, uint*, void**, int*, int>)
                Vtbl(face, SlotTryGetFontTable);
            var releaseTable = (delegate* unmanaged[Stdcall]<nint, void*, void>)Vtbl(face, SlotReleaseFontTable);

            void* table = null;
            uint size = 0;
            void* context = null;
            int exists = 0;
            if (tryGetTable(face, TagCmap, &table, &size, &context, &exists) < 0 || exists == 0 || table == null)
                return;

            try
            {
                var p = (byte*)table;
                if (size < 4) return;
                ushort numTables = ReadU16(p, 2);

                for (int i = 0; i < numTables; i++)
                {
                    int rec = 4 + 8 * i;
                    if (rec + 8 > size) break;
                    // All offsets come from the font file, so bounds math is done in long -
                    // uint/int arithmetic could wrap on a corrupt font, pass the check, and
                    // read outside the memory-mapped table (an uncatchable access violation).
                    uint subOffset = ReadU32(p, rec + 4);
                    if (subOffset > int.MaxValue || subOffset + 2L > size) continue;

                    ushort format = ReadU16(p, (int)subOffset);
                    if (format == 4)
                    {
                        if (subOffset + 8L > size) continue;
                        ushort segX2 = ReadU16(p, (int)subOffset + 6);
                        int seg = segX2 / 2;
                        long endBase = subOffset + 14L;
                        long startBase = endBase + segX2 + 2; // +2 skips reservedPad
                        if (startBase + segX2 > size) continue;
                        for (int s = 0; s < seg; s++)
                        {
                            ushort end = ReadU16(p, (int)(endBase + 2 * s));
                            ushort start = ReadU16(p, (int)(startBase + 2 * s));
                            if (start <= HebrewAlef && HebrewAlef <= end) hasHebrew = true;
                            if (start <= TeamimEtnahta && TeamimEtnahta <= end) hasTeamim = true;
                            if (hasHebrew && hasTeamim) return;
                        }
                    }
                    else if (format == 12)
                    {
                        if (subOffset + 16L > size) continue;
                        uint nGroups = ReadU32(p, (int)subOffset + 12);
                        for (uint g = 0; g < nGroups; g++)
                        {
                            long gr = subOffset + 16L + 12L * g;
                            if (gr + 12 > size) break;
                            uint start = ReadU32(p, (int)gr);
                            uint end = ReadU32(p, (int)gr + 4);
                            if (start <= HebrewAlef && HebrewAlef <= end) hasHebrew = true;
                            if (start <= TeamimEtnahta && TeamimEtnahta <= end) hasTeamim = true;
                            if (hasHebrew && hasTeamim) return;
                        }
                    }
                }
            }
            finally { releaseTable(face, context); }
        }

        // OpenType tables are big-endian; the host is little-endian.
        private static unsafe ushort ReadU16(byte* p, int offset)
            => (ushort)((p[offset] << 8) | p[offset + 1]);

        private static unsafe uint ReadU32(byte* p, int offset)
            => ((uint)p[offset] << 24) | ((uint)p[offset + 1] << 16) | ((uint)p[offset + 2] << 8) | p[offset + 3];

        /// <summary>The family's en-us name, falling back to the first localized name. Matches what
        /// WPF's FontFamily.Source reported, so the dropdown spelling is unchanged.</summary>
        private static unsafe string? ReadFamilyName(nint family)
        {
            nint strings = 0;
            var getNames = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(family, SlotGetFamilyNames);
            if (getNames(family, &strings) < 0 || strings == 0) return null;

            try
            {
                uint index = 0;
                int exists = 0;
                var find = (delegate* unmanaged[Stdcall]<nint, ushort*, uint*, int*, int>)
                    Vtbl(strings, SlotFindLocaleName);
                fixed (char* locale = "en-us")
                {
                    // Not found → fall back to index 0, the family's default name.
                    if (find(strings, (ushort*)locale, &index, &exists) < 0 || exists == 0) index = 0;
                }

                uint length = 0;
                var getLength = (delegate* unmanaged[Stdcall]<nint, uint, uint*, int>)
                    Vtbl(strings, SlotGetStringLength);
                if (getLength(strings, index, &length) < 0 || length == 0) return null;

                // GetString writes length chars plus a NUL terminator.
                char[] buffer = new char[length + 1];
                var getString = (delegate* unmanaged[Stdcall]<nint, uint, ushort*, uint, int>)
                    Vtbl(strings, SlotGetString);
                fixed (char* p = buffer)
                {
                    if (getString(strings, index, (ushort*)p, length + 1) < 0) return null;
                }
                return new string(buffer, 0, (int)length);
            }
            finally { Release(strings); }
        }
    }
}
