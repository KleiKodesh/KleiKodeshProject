using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>One table an update patch may touch.</summary>
    /// <param name="Updatable">False: a conflicting row is left as is (DO NOTHING) rather than updated.</param>
    public sealed record SeforimLibraryPatchTable(string Name, string[] PrimaryKey, bool Updatable);

    /// <summary>
    /// The tables of seforim.db as the update patches know them. A FIXED CONTRACT with the
    /// producer: port of otzaria_library_updater models/patch_table_spec.dart — copy changes
    /// from there, never reorder by hand.
    ///
    /// <see cref="InForeignKeyOrder"/> drives the apply (upserts in this order, deletes in
    /// reverse). <see cref="HashOrderSchema6"/> drives the content hash a patch is verified
    /// with; a different order gives a different hash.
    /// </summary>
    public static class SeforimLibraryPatchTables
    {
        public static readonly IReadOnlyList<SeforimLibraryPatchTable> InForeignKeyOrder = new[]
        {
            new SeforimLibraryPatchTable("source", new[] { "id" }, true),
            new SeforimLibraryPatchTable("author", new[] { "id" }, true),
            new SeforimLibraryPatchTable("topic", new[] { "id" }, true),
            new SeforimLibraryPatchTable("pub_place", new[] { "id" }, true),
            new SeforimLibraryPatchTable("pub_date", new[] { "id" }, true),
            new SeforimLibraryPatchTable("connection_type", new[] { "id" }, true),
            new SeforimLibraryPatchTable("tocText", new[] { "id" }, true),
            new SeforimLibraryPatchTable("generation", new[] { "id" }, true),
            new SeforimLibraryPatchTable("category", new[] { "id" }, true),
            new SeforimLibraryPatchTable("category_closure", new[] { "ancestorId", "descendantId" }, false),
            new SeforimLibraryPatchTable("book", new[] { "id" }, true),
            new SeforimLibraryPatchTable("book_author", new[] { "bookId", "authorId" }, false),
            new SeforimLibraryPatchTable("book_base_text", new[] { "bookId", "baseBookId" }, false),
            new SeforimLibraryPatchTable("book_topic", new[] { "bookId", "topicId" }, false),
            new SeforimLibraryPatchTable("book_pub_place", new[] { "bookId", "pubPlaceId" }, false),
            new SeforimLibraryPatchTable("book_pub_date", new[] { "bookId", "pubDateId" }, false),
            new SeforimLibraryPatchTable("book_acronym", new[] { "bookId", "term" }, false),
            new SeforimLibraryPatchTable("book_generation", new[] { "bookId", "generationId" }, false),
            new SeforimLibraryPatchTable("tocEntry", new[] { "id" }, true),
            new SeforimLibraryPatchTable("line", new[] { "id" }, true),
            new SeforimLibraryPatchTable("line_content", new[] { "id" }, true),
            new SeforimLibraryPatchTable("zstd_dict", new[] { "id" }, true),
            new SeforimLibraryPatchTable("line_toc", new[] { "lineId" }, true),
            new SeforimLibraryPatchTable("line_ref", new[] { "bookId", "refKeyHash", "lineIndex" }, false),
            new SeforimLibraryPatchTable("line_dh", new[] { "bookId", "dhText", "lineIndex" }, true),
            new SeforimLibraryPatchTable("link", new[] { "id" }, true),
            new SeforimLibraryPatchTable("link_anchor", new[] { "linkId", "side", "charStart" }, true),
            new SeforimLibraryPatchTable("link_range", new[] { "linkId", "side" }, true),
            new SeforimLibraryPatchTable("link_coverage", new[] { "lineId", "linkId", "side" }, false),
            new SeforimLibraryPatchTable("link_suppressed_side", new[] { "linkId", "side" }, true),
            new SeforimLibraryPatchTable("book_has_links", new[] { "bookId" }, true),
            new SeforimLibraryPatchTable("book_version", new[] { "id" }, true),
            new SeforimLibraryPatchTable("version_line", new[] { "versionId", "lineId" }, true),
            new SeforimLibraryPatchTable("alt_toc_structure", new[] { "id" }, true),
            new SeforimLibraryPatchTable("alt_toc_entry", new[] { "id" }, true),
            new SeforimLibraryPatchTable("line_alt_toc", new[] { "lineId", "structureId" }, true),
            new SeforimLibraryPatchTable("default_commentator", new[] { "bookId", "commentatorBookId" }, true),
            new SeforimLibraryPatchTable("default_targum", new[] { "bookId", "targumBookId" }, true),
            new SeforimLibraryPatchTable("banner", new[] { "id" }, true),
            new SeforimLibraryPatchTable("book_banner", new[] { "bookId" }, true),
            new SeforimLibraryPatchTable("book_protection", new[] { "bookId" }, true),
            new SeforimLibraryPatchTable("line_divine_name", new[] { "lineId", "charStart" }, true),
            new SeforimLibraryPatchTable("line_note", new[] { "lineId", "noteIndex" }, true),
            new SeforimLibraryPatchTable("line_note_divine_name", new[] { "lineId", "noteIndex", "charStart" }, true),
            new SeforimLibraryPatchTable("schema_meta", new[] { "key" }, true),
        };

        /// <summary>Table order of the schema-6 content hash (kHashTableOrderSchema6). Rows of a
        /// schema-6 table hash in id order, or by every column when it has no id.</summary>
        public static readonly IReadOnlyList<string> HashOrderSchema6 = new[]
        {
            "source", "author", "topic", "pub_place", "pub_date", "connection_type", "generation",
            "category", "category_closure", "tocText", "book", "book_topic", "book_author",
            "book_base_text", "book_pub_place", "book_pub_date", "book_generation", "tocEntry",
            "line", "line_content", "zstd_dict", "line_toc", "line_ref", "line_dh", "link",
            "link_anchor", "link_range", "link_coverage", "link_suppressed_side", "book_has_links",
            "book_version", "version_line", "book_acronym", "alt_toc_structure", "alt_toc_entry",
            "line_alt_toc", "default_commentator", "default_targum", "schema_meta",
        };

        /// <summary>The optimizer statistics a patch ships, applied over sqlite_stat1.</summary>
        public const string Stat1SnapshotTable = "stat1_snapshot";
    }
}
