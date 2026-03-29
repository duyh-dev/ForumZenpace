using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ForumZenpace.Services
{
    public sealed class AuthFlowTokenService
    {
        private readonly ITimeLimitedDataProtector _protector;

        public AuthFlowTokenService(IDataProtectionProvider dataProtectionProvider)
        {
            _protector = dataProtectionProvider
                .CreateProtector("ForumZenpace.AuthFlowTokens")
                .ToTimeLimitedDataProtector();
        }

        public string CreateEmailVerificationToken(int userId, TimeSpan lifetime)
        {
            return CreateToken(AuthFlowPurpose.VerifyEmail, userId, lifetime);
        }

        public string CreateRegistrationVerificationToken(int pendingRegistrationId, TimeSpan lifetime)
        {
            return CreateToken(AuthFlowPurpose.VerifyRegistration, pendingRegistrationId, lifetime);
        }

        public string CreatePasswordResetToken(int userId, TimeSpan lifetime)
        {
            return CreateToken(AuthFlowPurpose.ResetPassword, userId, lifetime);
        }

        public bool TryReadEmailVerificationToken(string? token, out int userId)
        {
            return TryReadToken(token, AuthFlowPurpose.VerifyEmail, out userId);
        }

        public bool TryReadRegistrationVerificationToken(string? token, out int pendingRegistrationId)
        {
            return TryReadToken(token, AuthFlowPurpose.VerifyRegistration, out pendingRegistrationId);
        }

        public bool TryReadPasswordResetToken(string? token, out int userId)
        {
            return TryReadToken(token, AuthFlowPurpose.ResetPassword, out userId);
        }

        private string CreateToken(AuthFlowPurpose purpose, int subjectId, TimeSpan lifetime)
        {
            return _protector.Protect($"{purpose}:{subjectId}", lifetime);
        }

        private bool TryReadToken(string? token, AuthFlowPurpose expectedPurpose, out int subjectId)
        {
            subjectId = 0;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var payload = _protector.Unprotect(token);
                var parts = payload.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2
                    || !Enum.TryParse(parts[0], out AuthFlowPurpose actualPurpose)
                    || actualPurpose != expectedPurpose
                    || !int.TryParse(parts[1], out subjectId)
                    || subjectId <= 0)
                {
                    subjectId = 0;
                    return false;
                }

                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private enum AuthFlowPurpose
        {
            VerifyRegistration,
            VerifyEmail,
            ResetPassword
        }
    }
}
