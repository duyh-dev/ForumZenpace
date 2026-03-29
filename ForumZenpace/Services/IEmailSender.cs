namespace ForumZenpace.Services
{
    public interface IEmailSender
    {
        Task SendVerificationOtpAsync(
            string toEmail,
            string recipientName,
            string otpCode,
            DateTime expiresAt,
            CancellationToken cancellationToken = default);

        Task SendPasswordResetOtpAsync(
            string toEmail,
            string recipientName,
            string otpCode,
            DateTime expiresAt,
            CancellationToken cancellationToken = default);
    }
}
