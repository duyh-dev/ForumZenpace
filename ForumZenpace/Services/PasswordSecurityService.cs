using ForumZenpace.Models;
using Microsoft.AspNetCore.Identity;

namespace ForumZenpace.Services
{
    public sealed class PasswordSecurityService
    {
        private static readonly object PasswordHasherUser = new();
        private readonly PasswordHasher<object> _passwordHasher = new();

        public string HashPassword(string password)
        {
            return _passwordHasher.HashPassword(PasswordHasherUser, password);
        }

        public bool IsHashed(string? storedPassword)
        {
            return !string.IsNullOrWhiteSpace(storedPassword)
                && storedPassword.StartsWith("AQAAAA", StringComparison.Ordinal);
        }

        public bool VerifyPassword(User user, string providedPassword, out bool shouldUpgradeHash)
        {
            shouldUpgradeHash = false;

            if (!IsHashed(user.Password))
            {
                var isLegacyMatch = string.Equals(user.Password, providedPassword, StringComparison.Ordinal);
                shouldUpgradeHash = isLegacyMatch;
                return isLegacyMatch;
            }

            var result = _passwordHasher.VerifyHashedPassword(PasswordHasherUser, user.Password, providedPassword);
            shouldUpgradeHash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result != PasswordVerificationResult.Failed;
        }

        public string EnsureHashedPassword(string storedPassword)
        {
            return IsHashed(storedPassword)
                ? storedPassword
                : HashPassword(storedPassword);
        }
    }
}
