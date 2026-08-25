using System;
using System.IO;
using System.Text;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Works out how a text file is encoded instead of assuming UTF-8.
    ///
    /// A BOM is authoritative when present. Without one, the bytes are validated as UTF-8, and
    /// anything that is not well-formed UTF-8 is read as Windows-1255 — the codepage Hebrew
    /// .txt files were written in for twenty years. Guessing wrong here does not produce a
    /// warning, it produces a file of mojibake, which is why this is a real check rather than
    /// a default.
    ///
    /// ⚠ CODEPAGE 1255 NEEDS A PROVIDER ON .NET 5+. <c>Encoding.GetEncoding(1255)</c> throws
    /// <see cref="NotSupportedException"/> on the modern runtime unless
    /// <c>CodePagesEncodingProvider</c> has been registered — a trap that only shows up on the
    /// service leg, at the moment a user opens one legacy file. Registration happens once here,
    /// which is why decoding lives in this class instead of at each call site.
    /// </summary>
    public static class TextEncodingDetector
    {
        /// <summary>Hebrew ANSI — the fallback for bytes that are not valid UTF-8.</summary>
        public const int HebrewAnsiCodePage = 1255;

        /// <summary>What the two hosts label a detected encoding on the wire.</summary>
        public const string Utf8Label = "utf-8";
        public const string HebrewAnsiLabel = "windows-1255";

        private static readonly object ProviderGate = new object();
        private static bool _providerRegistered;

        /// <summary>
        /// Reads a text file and decodes it with the encoding it actually uses.
        /// </summary>
        public static string ReadAllText(string filePath) => Decode(File.ReadAllBytes(filePath));

        /// <summary>
        /// Decodes bytes with the encoding they actually use, stripping a BOM if there is one.
        /// </summary>
        public static string Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0) return "";

            // A BOM settles it — no need to inspect the rest.
            if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            // UTF-32 LE must be tested BEFORE UTF-16 LE: FF FE 00 00 is a valid prefix of both,
            // and reading it as UTF-16 would decode the whole file one plane wrong.
            if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
                return new UTF32Encoding(false, false).GetString(bytes, 4, bytes.Length - 4);
            if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
                return new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);
            if (StartsWith(bytes, 0xFF, 0xFE))
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (StartsWith(bytes, 0xFE, 0xFF))
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            return IsValidUtf8(bytes)
                ? new UTF8Encoding(false).GetString(bytes)
                : HebrewAnsi().GetString(bytes);
        }

        /// <summary>
        /// The charset label for a HEAD of a file, for a caller that is about to serve the bytes
        /// rather than decode them (an HTTP Content-Type, say).
        /// </summary>
        /// <param name="head">The first bytes of the file.</param>
        /// <param name="truncated">True when <paramref name="head"/> is a prefix rather than the
        /// whole file. A prefix can end mid-sequence, and that trailing fragment is not evidence
        /// of anything — it gets dropped before the check, or a UTF-8 file cut at the wrong byte
        /// would be reported as legacy.</param>
        public static string DetectCharset(byte[] head, bool truncated)
        {
            if (head == null || head.Length == 0) return Utf8Label;

            int length = head.Length;
            if (truncated)
            {
                int i = length - 1;
                while (i >= 0 && (head[i] & 0xC0) == 0x80) i--;   // walk back over continuations
                if (i >= 0 && head[i] > 0x7F) length = i;          // ...to the lead byte, and cut it
            }

            return IsValidUtf8(head, length) ? Utf8Label : HebrewAnsiLabel;
        }

        /// <summary>
        /// True when the bytes are well-formed UTF-8 — pure ASCII counts. Rejects overlong
        /// encodings, surrogate code points and anything past U+10FFFF, all of which a naive
        /// length-based check would wave through and which a legacy codepage produces readily.
        /// </summary>
        public static bool IsValidUtf8(byte[] bytes) =>
            bytes == null || IsValidUtf8(bytes, bytes.Length);

        private static bool IsValidUtf8(byte[] bytes, int length)
        {
            int i = 0;
            while (i < length)
            {
                byte lead = bytes[i];
                int continuations;   // bytes expected after the lead
                int lowestLegal;     // smallest code point this length may encode (rejects overlong)

                if (lead <= 0x7F) { i++; continue; }
                else if ((lead & 0xE0) == 0xC0) { continuations = 1; lowestLegal = 0x80; }
                else if ((lead & 0xF0) == 0xE0) { continuations = 2; lowestLegal = 0x800; }
                else if ((lead & 0xF8) == 0xF0) { continuations = 3; lowestLegal = 0x10000; }
                else return false;   // a continuation byte where a lead belongs, or 0xF8+

                if (i + continuations >= length) return false;

                int codePoint = lead & (0x7F >> (continuations + 1));
                for (int k = 1; k <= continuations; k++)
                {
                    byte next = bytes[i + k];
                    if ((next & 0xC0) != 0x80) return false;
                    codePoint = (codePoint << 6) | (next & 0x3F);
                }

                if (codePoint < lowestLegal) return false;
                if (codePoint > 0x10FFFF) return false;
                if (codePoint >= 0xD800 && codePoint <= 0xDFFF) return false;  // lone surrogate

                i += continuations + 1;
            }
            return true;
        }

        /// <summary>
        /// Windows-1255, registering the codepage provider on first use so the modern runtime
        /// can produce it. Registration is process-wide and idempotent; the lock only stops two
        /// threads racing to be first.
        /// </summary>
        private static Encoding HebrewAnsi()
        {
            if (!_providerRegistered)
            {
                lock (ProviderGate)
                {
                    if (!_providerRegistered)
                    {
#if NET5_0_OR_GREATER
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
                        _providerRegistered = true;
                    }
                }
            }

            return Encoding.GetEncoding(HebrewAnsiCodePage);
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }
    }
}
