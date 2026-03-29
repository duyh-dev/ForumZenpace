namespace ForumZenpace.Models
{
    public class EmailJsSettings
    {
        public string ApiBaseUrl { get; set; } = "https://api.emailjs.com/api/v1.0";
        public string ServiceId { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string PasswordResetTemplateId { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public string FromName { get; set; } = "Zenpace";
        public string CompanyName { get; set; } = "Zenpace";
    }
}
