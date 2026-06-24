using System.Security.Cryptography;
using System.Text;

namespace TurizmOperasyonSistemi.Services
{
    public class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 20;
        private const int Iterations = 10000;
        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

        public static string HashPassword(string password)
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] salt = new byte[SaltSize];
                rng.GetBytes(salt);

                var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, Algorithm);
                byte[] hash = pbkdf2.GetBytes(HashSize);

                byte[] hashWithSalt = new byte[SaltSize + HashSize];
                Array.Copy(salt, 0, hashWithSalt, 0, SaltSize);
                Array.Copy(hash, 0, hashWithSalt, SaltSize, HashSize);

                return Convert.ToBase64String(hashWithSalt);
            }
        }

        public static bool VerifyPassword(string password, string hash)
        {
            try
            {
                byte[] hashWithSalt = Convert.FromBase64String(hash);

                if (hashWithSalt.Length != SaltSize + HashSize)
                    return false;

                byte[] salt = new byte[SaltSize];
                Array.Copy(hashWithSalt, 0, salt, 0, SaltSize);

                var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, Algorithm);
                byte[] computedHash = pbkdf2.GetBytes(HashSize);

                for (int i = 0; i < HashSize; i++)
                {
                    if (hashWithSalt[i + SaltSize] != computedHash[i])
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
