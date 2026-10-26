using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// The logical content hash a patch manifest is verified against: a SHA-256 over every
    /// table's rows, byte-for-byte the stream SeforimLibrary's LogicalContentHasher.kt writes.
    /// Port of otzaria_library_updater services/logical_content_hasher.dart.
    ///
    /// THE STREAM — a fixed contract, change nothing:
    ///   per table, in hash order:  " table:&lt;name&gt; "                (even when the table is missing)
    ///   if the table exists:        "cols:&lt;sorted,names&gt;" 0x00
    ///   per row (schema 6: ORDER BY id, else every sorted column):
    ///     per cell: tag + payload + 0x1F
    ///       NULL 0x00 | BLOB 0x01 + bytes | number 0x02 + decimal text | TEXT 0x03 + raw UTF-8
    ///     then 0xFF
    /// A table's own hash is the SHA-256 of exactly its segment of that stream.
    /// </summary>
    public static class SeforimLibraryContentHasher
    {
        private const byte NullTag = 0x00, BlobTag = 0x01, NumberTag = 0x02, TextTag = 0x03;
        private const byte UnitSeparator = 0x1F, RowSeparator = 0xFF;
        private const int ColumnsPerMask = 20;

        /// <summary>The SHA-256 of each table in <paramref name="tables"/>, lower-case hex.</summary>
        public static Dictionary<string, string> HashTables(SqliteConnection connection, IEnumerable<string> tables)
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string table in tables)
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var sink = new HashSink(sha);
                HashTable(connection, table, sink);
                sink.Flush();
                hashes[table] = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }
            return hashes;
        }

        private static void HashTable(SqliteConnection connection, string table, HashSink sink)
        {
            sink.Add(Encoding.UTF8.GetBytes(" table:" + table + " "));
            List<string>? columns = SortedColumns(connection, table);
            if (columns == null) return;

            sink.Add(Encoding.UTF8.GetBytes("cols:" + string.Join(",", columns)));
            sink.Add(NullTag);

            var masks = MaskExpressions(columns);
            string select = string.Join(",", masks.Concat(columns.Select(ValueExpression)));
            string orderBy = columns.Contains("id") ? "id" : string.Join(",", columns.Select(Quote));

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {select} FROM {Quote(table)} ORDER BY {orderBy}";
            using var reader = command.ExecuteReader();
            int maskCount = masks.Count;
            while (reader.Read())
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    int ordinal = maskCount + i;
                    if (reader.IsDBNull(ordinal))
                    {
                        sink.Add(NullTag);
                    }
                    else
                    {
                        object value = reader.GetValue(ordinal);
                        if (value is byte[] bytes)
                        {
                            long mask = reader.GetInt64(i / ColumnsPerMask);
                            sink.Add(((mask >> (i % ColumnsPerMask)) & 1) == 1 ? BlobTag : TextTag);
                            sink.Add(bytes);
                        }
                        else
                        {
                            sink.Add(NumberTag);
                            sink.Add(Encoding.ASCII.GetBytes(NumberText(value)));
                        }
                    }
                    sink.Add(UnitSeparator);
                }
                sink.Add(RowSeparator);
            }
        }

        /// <summary>Text is read as its raw bytes (CAST AS BLOB): decoding would drop a leading
        /// U+FEFF and change the hash.</summary>
        private static string ValueExpression(string column) =>
            $"CASE WHEN typeof({Quote(column)})='text' THEN CAST({Quote(column)} AS BLOB) ELSE {Quote(column)} END";

        /// <summary>One integer per 20 columns, bit i set when column i holds a blob — tells a
        /// real blob from text that was cast to one.</summary>
        private static List<string> MaskExpressions(List<string> columns)
        {
            var masks = new List<string>();
            for (int start = 0; start < columns.Count; start += ColumnsPerMask)
            {
                int end = Math.Min(start + ColumnsPerMask, columns.Count);
                var parts = new List<string>();
                for (int i = start; i < end; i++)
                    parts.Add($"((typeof({Quote(columns[i])})='blob')<<{i - start})");
                masks.Add(string.Join("|", parts));
            }
            return masks;
        }

        private static List<string>? SortedColumns(SqliteConnection connection, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({Quote(table)})";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(1));
            if (names.Count == 0) return null;
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static string NumberText(object value) => value switch
        {
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => JavaDoubleToString(d),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };

        /// <summary>Java's Double.toString — what the producer writes for a REAL:
        /// "1.0E-4" and "1.0E7" where .NET writes "0.0001" and "10000000".</summary>
        internal static string JavaDoubleToString(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "Infinity";
            if (double.IsNegativeInfinity(value)) return "-Infinity";
            if (value == 0) return "0.0";

            // Shortest round-trip digits and the exponent of the first one.
            string shortest = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
            (string digits, int exp) = DigitsAndExponent(shortest);

            string body;
            if (exp >= 0 && exp < 7)
            {
                int intLen = exp + 1;
                body = digits.Length > intLen
                    ? digits.Substring(0, intLen) + "." + digits.Substring(intLen)
                    : digits.PadRight(intLen, '0') + ".0";
            }
            else if (exp < 0 && exp >= -3)
            {
                body = "0." + new string('0', -exp - 1) + digits;
            }
            else
            {
                string frac = digits.Length > 1 ? digits.Substring(1) : "0";
                body = digits[0] + "." + frac + "E" + exp.ToString(CultureInfo.InvariantCulture);
            }
            return value < 0 ? "-" + body : body;
        }

        /// <summary>"123.45" -> ("12345", 2); "1E-05" -> ("1", -5). Trailing zeros dropped.</summary>
        private static (string Digits, int Exponent) DigitsAndExponent(string text)
        {
            int e = text.IndexOfAny(new[] { 'E', 'e' });
            int exp10 = e >= 0 ? int.Parse(text.Substring(e + 1), CultureInfo.InvariantCulture) : 0;
            string mantissa = e >= 0 ? text.Substring(0, e) : text;
            int dot = mantissa.IndexOf('.');
            int intDigits = dot >= 0 ? dot : mantissa.Length;
            string all = mantissa.Replace(".", "");
            int lead = 0;
            while (lead < all.Length - 1 && all[lead] == '0') lead++;
            string digits = all.Substring(lead).TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            return (digits, intDigits - 1 - lead + exp10);
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

        /// <summary>Batches the many tiny writes into ~1 MB hash updates. SHA-256 does not
        /// depend on chunk boundaries, so batching does not change the result.</summary>
        private sealed class HashSink
        {
            private readonly IncrementalHash _sha;
            private readonly byte[] _buffer = new byte[1 << 20];
            private int _length;

            public HashSink(IncrementalHash sha) { _sha = sha; }

            public void Add(byte value)
            {
                if (_length == _buffer.Length) Flush();
                _buffer[_length++] = value;
            }

            public void Add(byte[] bytes)
            {
                if (bytes.Length >= _buffer.Length)
                {
                    Flush();
                    _sha.AppendData(bytes);
                    return;
                }
                if (_length + bytes.Length > _buffer.Length) Flush();
                Buffer.BlockCopy(bytes, 0, _buffer, _length, bytes.Length);
                _length += bytes.Length;
            }

            public void Flush()
            {
                if (_length == 0) return;
                _sha.AppendData(_buffer, 0, _length);
                _length = 0;
            }
        }
    }
}
