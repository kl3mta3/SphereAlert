using System.Security.Cryptography;
using System.Text;

namespace SphereAlert.Services.Security
{
    /// <summary>
    /// Password hashing — ported from SphereSSL. PBKDF2-SHA256 with 100,000 iterations
    /// over a SHA-512 prehash of the password, 16-byte random salt, 32-byte derived key.
    /// Stored format is Base64(salt[16] + hash[32]).
    /// </summary>
    public class PasswordService
    {
        private const int Iterations = 100_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static string HashPassword(string password)
        {
            string prehashed = Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes(password)));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

            using var pbkdf2 = new Rfc2898DeriveBytes(prehashed, salt, Iterations, HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(HashSize);

            byte[] combined = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, combined, 0, SaltSize);
            Array.Copy(hash, 0, combined, SaltSize, HashSize);

            return Convert.ToBase64String(combined);
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                byte[] combined = Convert.FromBase64String(storedHash);
                if (combined.Length != SaltSize + HashSize)
                    return false;

                byte[] salt = new byte[SaltSize];
                Array.Copy(combined, 0, salt, 0, SaltSize);

                string prehashed = Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes(password)));
                using var pbkdf2 = new Rfc2898DeriveBytes(prehashed, salt, Iterations, HashAlgorithmName.SHA256);
                byte[] hash = pbkdf2.GetBytes(HashSize);

                byte[] storedKey = new byte[HashSize];
                Array.Copy(combined, SaltSize, storedKey, 0, HashSize);

                return CryptographicOperations.FixedTimeEquals(hash, storedKey);
            }
            catch
            {
                return false;
            }
        }
    }
}
