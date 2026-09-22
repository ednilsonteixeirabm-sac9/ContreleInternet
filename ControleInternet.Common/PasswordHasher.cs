using System;
using System.Security.Cryptography;

namespace ControleInternet.Common
{
    public static class PasswordHasher
    {
        public const int DefaultIterations = 100000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static void SetPassword(AppConfig config, string password)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            ValidatePassword(password);

            byte[] salt = new byte[SaltSize];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
            }

            byte[] hash = Derive(password, salt, DefaultIterations);
            config.PasswordSalt = Convert.ToBase64String(salt);
            config.PasswordHash = Convert.ToBase64String(hash);
            config.PasswordIterations = DefaultIterations;
        }

        public static bool Verify(AppConfig config, string password)
        {
            if (config == null || !config.HasPassword || password == null)
            {
                return false;
            }

            try
            {
                byte[] salt = Convert.FromBase64String(config.PasswordSalt);
                byte[] expected = Convert.FromBase64String(config.PasswordHash);
                byte[] actual = Derive(password, salt, config.PasswordIterations);
                return FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public static void ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                throw new ArgumentException("A senha deve possuir pelo menos 6 caracteres.", "password");
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(password, salt, iterations))
            {
                return derive.GetBytes(HashSize);
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            int difference = left.Length ^ right.Length;
            int length = Math.Min(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }
}
