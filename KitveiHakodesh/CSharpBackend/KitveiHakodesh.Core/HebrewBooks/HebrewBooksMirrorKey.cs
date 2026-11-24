using System;
using System.Security.Cryptography;
using System.Text;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Opens the sealed mirror settings (<c>r2Sealed</c> in hb-mirror.json). The release build
    /// (Build/seal-hb-mirror.ps1) makes a fresh random AES-256 key per release, seals the bucket
    /// settings with AES-GCM, and writes the key - split into two XOR halves - into the generated
    /// HebrewBooksMirrorKey.g.cs, which is compiled into the native service exe and never in git.
    ///
    /// This keeps the keys out of plain text in the installer. It is obfuscation, not secrecy:
    /// a determined user can still recover them from the exe.
    ///
    /// Without the generated file (dev builds) <see cref="Open"/> returns null and the bucket
    /// fallback is simply skipped.
    /// </summary>
    internal static partial class HebrewBooksMirrorKey
    {
        static partial void Fill(ref byte[]? left, ref byte[]? right);

        /// <summary>The settings JSON inside a sealed blob, or null when it cannot be opened.</summary>
        public static string? Open(string sealedBase64)
        {
            byte[]? left = null, right = null;
            Fill(ref left, ref right);
            if (left == null || right == null || left.Length != 32 || right.Length != 32) return null;

            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(left[i] ^ right[i]);
            try
            {
                byte[] blob = Convert.FromBase64String(sealedBase64);
                if (blob.Length < 12 + 16) return null;
                var nonce = blob.AsSpan(0, 12);
                var tag = blob.AsSpan(blob.Length - 16, 16);
                var cipher = blob.AsSpan(12, blob.Length - 28);
                byte[] plain = new byte[cipher.Length];
                using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException)
            {
                return null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
