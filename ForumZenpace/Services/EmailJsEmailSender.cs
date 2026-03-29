using System.Net.Http.Json;
using ForumZenpace.Models;
using Microsoft.Extensions.Options;

namespace ForumZenpace.Services
{
    public class EmailJsEmailSender : IEmailSender
    {
        private readonly HttpClient _httpClient;
        private readonly EmailJsSettings _settings;

        public EmailJsEmailSender(HttpClient httpClient, IOptions<EmailJsSettings> options)
        {
            _httpClient = httpClient;
            _settings = options.Value;
        }

        public async Task SendVerificationOtpAsync(
            string toEmail,
            string recipientName,
            string otpCode,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            await SendOtpAsync(
                toEmail,
                recipientName,
                otpCode,
                expiresAt,
                string.IsNullOrWhiteSpace(_settings.TemplateId) ? null : _settings.TemplateId,
                "xac thuc email",
                cancellationToken);
        }

        public async Task SendPasswordResetOtpAsync(
            string toEmail,
            string recipientName,
            string otpCode,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            await SendOtpAsync(
                toEmail,
                recipientName,
                otpCode,
                expiresAt,
                string.IsNullOrWhiteSpace(_settings.PasswordResetTemplateId) ? _settings.TemplateId : _settings.PasswordResetTemplateId,
                "dat lai mat khau",
                cancellationToken);
        }

        private async Task SendOtpAsync(
            string toEmail,
            string recipientName,
            string otpCode,
            DateTime expiresAt,
            string? templateId,
            string purposeLabel,
            CancellationToken cancellationToken)
        {
            var missingSettings = new List<string>();
            if (string.IsNullOrWhiteSpace(_settings.ServiceId))
            {
                missingSettings.Add(nameof(_settings.ServiceId));
            }

            if (string.IsNullOrWhiteSpace(templateId))
            {
                missingSettings.Add(nameof(_settings.TemplateId));
            }

            if (string.IsNullOrWhiteSpace(_settings.PublicKey))
            {
                missingSettings.Add(nameof(_settings.PublicKey));
            }

            if (missingSettings.Count > 0)
            {
                throw new InvalidOperationException($"EmailJsSettings chua duoc cau hinh day du: {string.Join(", ", missingSettings)}.");
            }

            var endpoint = $"{_settings.ApiBaseUrl.TrimEnd('/')}/email/send";
            var payload = new Dictionary<string, object>
            {
                ["service_id"] = _settings.ServiceId,
                ["template_id"] = templateId!,
                ["user_id"] = _settings.PublicKey,
                ["template_params"] = new Dictionary<string, string>
                {
                    ["email"] = toEmail,
                    ["to_email"] = toEmail,
                    ["toEmail"] = toEmail,
                    ["to_name"] = recipientName,
                    ["recipient_name"] = recipientName,
                    ["passcode"] = otpCode,
                    ["time"] = expiresAt.ToLocalTime().ToString("HH:mm dd/MM/yyyy"),
                    ["otp_purpose"] = purposeLabel,
                    ["subject"] = $"Zenpace OTP - {purposeLabel}",
                    ["action_label"] = purposeLabel,
                    ["from_name"] = _settings.FromName,
                    ["company_name"] = _settings.CompanyName,
                    ["app_name"] = _settings.CompanyName
                }
            };

            if (!string.IsNullOrWhiteSpace(_settings.PrivateKey))
            {
                payload["accessToken"] = _settings.PrivateKey;
            }

            using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"EmailJS gui that bai: {(int)response.StatusCode} {body}");
        }
    }
}
