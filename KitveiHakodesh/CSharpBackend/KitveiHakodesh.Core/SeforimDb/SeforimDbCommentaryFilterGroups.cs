using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The commentary panel's filter tree: every book that stands in a fixed relationship to
    /// the open one - what comments on / translates / expounds it (forward links), and what it
    /// is itself based on (reverse links, base-text rules of GetReverseBooks). One call, one
    /// connection; built on demand, nothing kept.
    ///
    /// The groups, their order, labels and categories are the related-books list's own
    /// <see cref="BuildStaticGroups"/> (formerly the frontend's buildStaticCommentaryFilterGroups).
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        /// <summary>" · " joining the parts of a group's path.</summary>
        private const string PathSeparator = " · ";

        public CommentaryFilterGroupList GetCommentaryFilterGroups(int bookId)
        {
            using var conn = Open();
            var typeNames = ReadConnectionTypeNames(conn);
            var typeIds = DependantTypeIds(typeNames);
            var result = new CommentaryFilterGroupList();
            if (typeIds.Count == 0) return result;

            var forward = ReadStaticFilterBooks(conn, bookId, typeIds).Select(r => (r.TargetBookId, r.ConnectionTypeId)).ToList();
            var reverse = ReadReverseBooks(conn, SchemaOf(conn), bookId, typeIds);
            var groups = BuildStaticGroups(forward, reverse, typeNames, BooksById(), CategoriesById(), new Dictionary<int, string>());

            foreach (var g in groups)
            {
                string path = g.Title + PathSeparator + g.SectionLabel;
                if (g.SubSectionLabel != null) path += PathSeparator + g.SubSectionLabel;
                result.Groups.Add(new CommentaryFilterGroup
                {
                    BookId = g.BookId,
                    BookTitle = g.Title,
                    Path = path,
                    ConnectionTypes = g.ConnectionTypes,
                    Category = g.Category,
                    SectionLabel = g.SectionLabel,
                    SubSectionLabel = g.SubSectionLabel,
                });
            }
            return result;
        }

        /// <summary>GetReverseBooks' rows (same SQL, same order) on an open connection.</summary>
        private static List<int> ReadReverseBooks(SqliteConnection conn, SeforimDbSchema schema, int bookId, List<int> typeIds)
        {
            var rows = new List<int>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetReverseBooks(schema, typeIds.Count);
            cmd.Parameters.AddWithValue("@bookId", bookId);
            BindList(cmd, "c", typeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add(r.GetInt32(0));
            return rows;
        }
    }
}
