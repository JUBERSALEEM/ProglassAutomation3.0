// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.a.5.UserPassword.cs
// MODULE: step3 — how a password is stored, and how it is checked.
//
// Owner instruction (2026-10-27): "user should have password so user should be
// able to read the data related to them only". Until this file existed there was
// no credential at all: identity was the Windows workstation account, which is
// fine on a single-user station and useless as soon as one person must not be
// able to act as another.
//
// What is stored
//   • PasswordSalt       16 random bytes, base64. Unique per user.
//   • PasswordHash       PBKDF2-HMAC-SHA256, 32 bytes, base64.
//   • PasswordIterations the work factor used, stored with the row so it can be
//                        raised later without invalidating old passwords.
//   Nothing reversible is stored, and the plain password never reaches the
//   database, a log line, the register, or a message.
//
// Why PBKDF2 and not a plain SHA-256
//   A fast hash is a list of hashes waiting to be reversed with a GPU. PBKDF2
//   with 210 000 iterations (the OWASP figure for this family) makes each guess
//   cost the attacker the same work it costs us to check a sign-in.
//
// Verification is constant-time (CryptographicOperations.FixedTimeEquals), so a
// wrong password cannot be narrowed down by timing.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ProGlassAutomation.Models.Users
{
    /// <summary>A stored password: salt, derived hash and the work factor used.</summary>
    public sealed class PasswordHash
    {
        public string Hash { get; init; } = "";

        public string Salt { get; init; } = "";

        public int Iterations { get; init; }

        /// <summary>True when the row has no usable credential yet.</summary>
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Hash) || string.IsNullOrWhiteSpace(Salt) || Iterations <= 0;

        public static readonly PasswordHash Empty = new();
    }

    /// <summary>Password policy and the one place a password is hashed or checked.</summary>
    public static class UserPassword
    {
        /// <summary>Short enough to be typeable, long enough to resist a dictionary.</summary>
        public const int MinimumLength = 8;

        public const int MaximumLength = 128;

        /// <summary>OWASP recommendation for PBKDF2-HMAC-SHA256 (2023).</summary>
        public const int DefaultIterations = 210_000;

        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        /// <summary>
        /// Field-level policy. Returns null when the password may be stored.
        /// </summary>
        /// <remarks>
        /// Deliberately simple and explainable: length, and at least one letter and
        /// one digit. Composition rules ("one symbol, one capital") are no longer
        /// recommended and produce `Password1!` every time.
        /// </remarks>
        public static string? Validate(string? password)
        {
            if (string.IsNullOrEmpty(password)) return "Password is required.";
            if (password.Length < MinimumLength)
                return $"Password must be at least {MinimumLength} characters.";
            if (password.Length > MaximumLength)
                return $"Password must be {MaximumLength} characters or fewer.";
            if (password.Trim().Length != password.Length)
                return "Password cannot start or end with a space.";
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
                return "Password must contain at least one letter and one digit.";
            return null;
        }

        /// <summary>Hashes a password with a fresh salt.</summary>
        public static PasswordHash Create(string password, int iterations = DefaultIterations)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);

            return new PasswordHash
            {
                Hash = Convert.ToBase64String(hash),
                Salt = Convert.ToBase64String(salt),
                Iterations = iterations
            };
        }

        /// <summary>Constant-time check of a typed password against a stored hash.</summary>
        public static bool Verify(string? password, PasswordHash? stored)
        {
            if (string.IsNullOrEmpty(password) || stored == null || stored.IsEmpty) return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(stored.Salt);
                expected = Convert.FromBase64String(stored.Hash);
            }
            catch (FormatException)
            {
                // A corrupted column is a failed sign-in, never an exception the
                // gate has to explain.
                return false;
            }

            if (salt.Length == 0 || expected.Length == 0) return false;

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, stored.Iterations,
                HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
