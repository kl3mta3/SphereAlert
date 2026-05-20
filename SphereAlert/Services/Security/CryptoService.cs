using System.Security.Cryptography;
using System.Text;
using SphereAlert.Services.Config;

namespace SphereAlert.Services.Security
{
    /// <summary>
    /// Symmetric encryption for DNS provider credentials at rest (AES-256-GCM).
    /// The 256-bit master key is generated once and persisted to a keyfile that
    /// lives beside the database in the data volume. Keeping the key in a separate
    /// file means a stray copy of the .db alone (committed to git, dropped into a
    /// backup, attached to a support bundle) does not expose any API tokens.
    ///
    /// On-disk ciphertext layout, Base64-encoded: nonce[12] + tag[16] + ciphertext.
    /// </summary>
    public static class CryptoService
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;

        private static byte[]? _key;
        private static readonly object _lock = new();

        private static byte[] GetKey()
        {
            if (_key != null) return _key;
            lock (_lock)
            {
                if (_key != null) return _key;

                string path = ConfigureService.KeyFilePath;
                if (File.Exists(path))
                {
                    _key = Convert.FromBase64String(File.ReadAllText(path).Trim());
                    if (_key.Length != KeySize)
                        throw new InvalidOperationException("Keyfile is corrupt or has the wrong length.");
                }
                else
                {
                    byte[] generated = RandomNumberGenerator.GetBytes(KeySize);
                    File.WriteAllText(path, Convert.ToBase64String(generated));
                    RestrictKeyFile(path);
                    _key = generated;
                }
                return _key;
            }
        }

        private static void RestrictKeyFile(string path)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Best effort — the data volume is already operator-owned.
            }
        }

        public static string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return string.Empty;

            byte[] key = GetKey();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] cipher = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plainBytes, cipher, tag);
            }

            byte[] output = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);

            return Convert.ToBase64String(output);
        }

        public static string Decrypt(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted))
                return string.Empty;

            byte[] key = GetKey();
            byte[] input = Convert.FromBase64String(encrypted);
            if (input.Length < NonceSize + TagSize)
                throw new InvalidOperationException("Ciphertext is too short to be valid.");

            byte[] nonce = new byte[NonceSize];
            byte[] tag = new byte[TagSize];
            byte[] cipher = new byte[input.Length - NonceSize - TagSize];
            Buffer.BlockCopy(input, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(input, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(input, NonceSize + TagSize, cipher, 0, cipher.Length);

            byte[] plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }

            return Encoding.UTF8.GetString(plain);
        }
    }
}
