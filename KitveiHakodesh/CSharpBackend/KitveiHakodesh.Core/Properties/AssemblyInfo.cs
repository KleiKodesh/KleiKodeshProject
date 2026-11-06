using System.Runtime.CompilerServices;

// Lets KitveiHakodeshService.Tests unit-test the pure parsing/counter logic in
// Settings/SeforimDbAdditionalLibraries.cs (ParseEntries, SerializeEntries, NextIndexFrom)
// directly, without going through the Windows registry — the same pattern FtsLib uses for
// FtsLibTest (see FtsLib/Properties/AssemblyInfo.cs).
[assembly: InternalsVisibleTo("KitveiHakodeshService.Tests")]
