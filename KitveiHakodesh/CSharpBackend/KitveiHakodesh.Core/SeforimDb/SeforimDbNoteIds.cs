using System;
using System.Data.Common;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Ids of the VIRTUAL "notes books" (issue #13): on a database with a <c>line_note</c> table
    /// (Otzaria schema 7) every book with footnotes gets a notes book right after it in catalog
    /// order, one line per note, so the full-text index finds note words like any other line.
    ///
    /// The ids are disjoint from real ones by construction and fit int32:
    /// <code>
    ///   R = 2^30                      every real line / book id must be below R
    ///   virtual line id = R + (lineId &lt;&lt; 6) + noteIndex   (lineId &lt; 2^24, noteIndex 0..63)
    ///   virtual book id = R + bookId
    /// </code>
    /// An id at or above R is virtual; decoding gives the SOURCE line, its note index and the
    /// source book, which is what a hit opens. The one place these constants live (Dart mirrors
    /// them in note_ids.dart).
    /// </summary>
    public static class SeforimDbNoteIds
    {
        /// <summary>R: the first reserved (virtual) id.</summary>
        public const int ReservedStart = 1 << 30;

        /// <summary>Bits of the note index inside a virtual line id.</summary>
        public const int NoteIndexBits = 6;

        /// <summary>Largest note index a virtual line id can hold (63).</summary>
        public const int MaxNoteIndex = (1 << NoteIndexBits) - 1;

        /// <summary>Largest source line id a virtual line id can hold (2^24 - 1).</summary>
        public const int MaxSourceLineId = (1 << (30 - NoteIndexBits)) - 1;

        /// <summary>True for a virtual (notes book) line or book id.</summary>
        public static bool IsVirtual(int id) => id >= ReservedStart;

        /// <summary>The virtual line id of note <paramref name="noteIndex"/> of line <paramref name="lineId"/>.</summary>
        public static int EncodeLine(int lineId, int noteIndex)
        {
            if (lineId < 0 || lineId > MaxSourceLineId)
                throw new ArgumentOutOfRangeException(nameof(lineId), lineId, "A line with notes must have an id below 2^24.");
            if (noteIndex < 0 || noteIndex > MaxNoteIndex)
                throw new ArgumentOutOfRangeException(nameof(noteIndex), noteIndex, "A note index must be 0..63.");
            return ReservedStart + (lineId << NoteIndexBits) + noteIndex;
        }

        /// <summary>The virtual book id of the notes book of <paramref name="bookId"/>.</summary>
        public static int EncodeBook(int bookId)
        {
            if (bookId < 0 || bookId >= ReservedStart)
                throw new ArgumentOutOfRangeException(nameof(bookId), bookId, "A book with notes must have an id in 0..2^30-1.");
            return ReservedStart + bookId;
        }

        /// <summary>Decodes a virtual line id into its source line and note index; false for a real id.</summary>
        public static bool TryDecodeLine(int id, out int sourceLineId, out int noteIndex)
        {
            if (!IsVirtual(id)) { sourceLineId = 0; noteIndex = 0; return false; }
            int v = id - ReservedStart;
            sourceLineId = v >> NoteIndexBits;
            noteIndex = v & MaxNoteIndex;
            return true;
        }

        /// <summary>Decodes a virtual book id into its source book; false for a real id.</summary>
        public static bool TryDecodeBook(int id, out int sourceBookId)
        {
            if (!IsVirtual(id)) { sourceBookId = 0; return false; }
            sourceBookId = id - ReservedStart;
            return true;
        }

        /// <summary>The line a hit opens: the source line of a note, the id itself otherwise.</summary>
        public static int SourceLineOf(int lineId) => TryDecodeLine(lineId, out int source, out _) ? source : lineId;

        /// <summary>The book a hit opens: the source book of a notes book, the id itself otherwise.</summary>
        public static int SourceBookOf(int bookId) => TryDecodeBook(bookId, out int source) ? source : bookId;

        /// <summary>
        /// THE GUARD, once per index build: fails loudly, naming the id, when the database holds
        /// an id the encoding cannot represent (a real line / book id at or above R, a note's
        /// line id at or above 2^24, a note index outside 0..63). Nulls (empty tables) pass.
        /// </summary>
        public static void EnsureRepresentable(long? maxLineId, long? maxBookId, long? maxNoteLineId, long? minNoteIndex, long? maxNoteIndex)
        {
            if (maxLineId >= ReservedStart)
                throw new SeforimDbNoteIdRangeException($"line.id {maxLineId} is at or above the reserved notes id range (2^30): the notes books cannot be indexed.");
            if (maxBookId >= ReservedStart)
                throw new SeforimDbNoteIdRangeException($"book.id {maxBookId} is at or above the reserved notes id range (2^30): the notes books cannot be indexed.");
            if (maxNoteLineId > MaxSourceLineId)
                throw new SeforimDbNoteIdRangeException($"line_note.lineId {maxNoteLineId} is at or above 2^24: its notes cannot get a virtual line id.");
            if (minNoteIndex < 0)
                throw new SeforimDbNoteIdRangeException($"line_note.noteIndex {minNoteIndex} is negative: its note cannot get a virtual line id.");
            if (maxNoteIndex > MaxNoteIndex)
                throw new SeforimDbNoteIdRangeException($"line_note.noteIndex {maxNoteIndex} is above 63: its note cannot get a virtual line id.");
        }

        /// <summary><see cref="EnsureRepresentable(long?, long?, long?, long?, long?)"/> over the
        /// database's own maxima. Call only when <c>line_note</c> exists.</summary>
        public static void EnsureRepresentable(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT (SELECT max(id) FROM line), (SELECT max(id) FROM book)," +
                " (SELECT max(lineId) FROM line_note), (SELECT min(noteIndex) FROM line_note), (SELECT max(noteIndex) FROM line_note)";
            using var r = command.ExecuteReader();
            r.Read();
            long? At(int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
            EnsureRepresentable(At(0), At(1), At(2), At(3), At(4));
        }
    }

    /// <summary>The database holds an id the notes-book encoding cannot represent (see
    /// <see cref="SeforimDbNoteIds.EnsureRepresentable(DbConnection)"/>): the index build stops.</summary>
    public sealed class SeforimDbNoteIdRangeException : Exception
    {
        public SeforimDbNoteIdRangeException(string message) : base(message) { }
    }
}
