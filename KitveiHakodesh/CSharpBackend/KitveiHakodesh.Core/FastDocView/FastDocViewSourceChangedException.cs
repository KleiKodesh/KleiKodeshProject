namespace KitveiHakodesh.Core.FastDocView;

/// <summary>The file changed on disk, or the document id is unknown / was closed when idle.
/// The message carries <see cref="Marker"/>: the page reopens the path when it sees it.</summary>
public sealed class FastDocViewSourceChangedException(string detail)
    : Exception($"{Marker}: {detail}")
{
    public const string Marker = "FastDocViewSourceChanged";
}
