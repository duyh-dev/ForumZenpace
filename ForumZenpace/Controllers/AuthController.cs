using System.Security.Claims;
using ForumZenpace.Models;
using ForumZenpace.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ForumZenpace.Controllers
{
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly ForumDbContext _context;
        private readonly EmailVerificationService _emailVerificationService;
        private readonly PasswordSecurityService _passwordSecurityService;
        private readonly AuthFlowTokenService _authFlowTokenService;

        public AuthController(
            ForumDbContext context,
            EmailVerificationService emailVerificationService,
            PasswordSecurityService passwordSecurityService,
            AuthFlowTokenService authFlowTokenService)
        {
            _context = context;
            _emailVerificationService = emailVerificationService;
            _passwordSecurityService = passwordSecurityService;
            _authFlowTokenService = authFlowTokenService;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            model.Username = model.Username?.Trim() ?? string.Empty;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users
                .Include(account => account.Role)
                .FirstOrDefaultAsync(account => account.Username == model.Username);

            if (user == null || !_passwordSecurityService.VerifyPassword(user, model.Password, out var shouldUpgradeHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password");
                return View(model);
            }

            if (shouldUpgradeHash)
            {
                user.Password = _passwordSecurityService.HashPassword(model.Password);
                await _context.SaveChangesAsync();
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Your account is locked.");
                return View(model);
            }

            if (!user.IsEmailConfirmed)
            {
                var otpResult = await IssueAndSendOtpAsync(user);
                var flowToken = CreateEmailVerificationFlowToken(user.Id);
                TempData[otpResult.Success ? "AuthSuccessMessage" : "AuthErrorMessage"] = otpResult.Success
                    ? "Tài khoản chưa xác thực email. Chúng tôi đã gửi mã OTP mới, vui lòng nhập mã để tiếp tục."
                    : BuildOtpFailureMessage(otpResult.ErrorMessage);
                return RedirectToAction(nameof(VerifyEmail), new { token = flowToken });
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role?.Name ?? "User")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            model.Username = model.Username?.Trim() ?? string.Empty;
            model.FullName = model.FullName?.Trim() ?? string.Empty;
            model.Email = model.Email?.Trim() ?? string.Empty;

            await CleanupExpiredPendingRegistrationsAsync();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (await _context.Users.AnyAsync(user => user.Username == model.Username))
            {
                ModelState.AddModelError(nameof(RegisterViewModel.Username), "Username is already taken.");
                return View(model);
            }

            if (await _context.Users.AnyAsync(user => user.Email == model.Email))
            {
                ModelState.AddModelError(nameof(RegisterViewModel.Email), "Email đã được sử dụng cho một tài khoản khác.");
                return View(model);
            }

            var pendingRegistration = await FindPendingRegistrationAsync(model);
            if (pendingRegistration is null)
            {
                pendingRegistration = new PendingRegistration
                {
                    Username = model.Username,
                    FullName = model.FullName,
                    Email = model.Email,
                    Password = _passwordSecurityService.HashPassword(model.Password)
                };

                _context.PendingRegistrations.Add(pendingRegistration);
            }
            else
            {
                if (!string.Equals(pendingRegistration.Username, model.Username, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pendingRegistration.Email, model.Email, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(RegisterViewModel.Email), "Email này đang được dùng cho một yêu cầu đăng ký khác.");
                    return View(model);
                }

                if (!string.Equals(pendingRegistration.Email, model.Email, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pendingRegistration.Username, model.Username, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(RegisterViewModel.Username), "Tên tài khoản này đang được dùng cho một yêu cầu đăng ký khác.");
                    return View(model);
                }

                pendingRegistration.Username = model.Username;
                pendingRegistration.FullName = model.FullName;
                pendingRegistration.Email = model.Email;
                pendingRegistration.Password = _passwordSecurityService.HashPassword(model.Password);
                pendingRegistration.UpdatedAt = DateTime.UtcNow;
            }

            var otpResult = await IssueAndSendOtpAsync(pendingRegistration);
            var flowToken = CreateRegistrationVerificationFlowToken(pendingRegistration.Id);
            TempData[otpResult.Success ? "AuthSuccessMessage" : "AuthErrorMessage"] = otpResult.Success
                ? "Hệ thống đã gửi mã OTP tới email của bạn. Hãy nhập đúng mã để hoàn tất đăng ký tài khoản."
                : BuildOtpFailureMessage(otpResult.ErrorMessage);

            return RedirectToAction(nameof(VerifyRegistration), new { token = flowToken });
        }

        [HttpGet]
        public async Task<IActionResult> VerifyRegistration(string token)
        {
            if (!_authFlowTokenService.TryReadRegistrationVerificationToken(token, out var pendingRegistrationId))
            {
                TempData["AuthErrorMessage"] = "Liên kết xác thực đăng ký không hợp lệ hoặc đã hết hạn.";
                return RedirectToAction(nameof(Register));
            }

            var pendingRegistration = await _context.PendingRegistrations.FindAsync(pendingRegistrationId);
            if (pendingRegistration == null)
            {
                TempData["AuthErrorMessage"] = "Không tìm thấy yêu cầu đăng ký cần xác thực.";
                return RedirectToAction(nameof(Register));
            }

            return View(new VerifyRegistrationOtpViewModel
            {
                FlowToken = token,
                Username = pendingRegistration.Username,
                EmailMask = EmailVerificationService.MaskEmail(pendingRegistration.Email)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyRegistration(VerifyRegistrationOtpViewModel model)
        {
            if (!_authFlowTokenService.TryReadRegistrationVerificationToken(model.FlowToken, out var pendingRegistrationId))
            {
                TempData["AuthErrorMessage"] = "Lien ket xac thuc dang ky khong hop le hoac da het han.";
                return RedirectToAction(nameof(Register));
            }

            var pendingRegistration = await _context.PendingRegistrations.FindAsync(pendingRegistrationId);
            if (pendingRegistration == null)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay yeu cau dang ky can xac thuc.";
                return RedirectToAction(nameof(Register));
            }

            model.Username = pendingRegistration.Username;
            model.EmailMask = EmailVerificationService.MaskEmail(pendingRegistration.Email);
            model.OtpCode = (model.OtpCode ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!_emailVerificationService.VerifyOtp(pendingRegistration, model.OtpCode))
            {
                ModelState.AddModelError(nameof(VerifyRegistrationOtpViewModel.OtpCode), "Ma OTP khong dung hoac da het han.");
                return View(model);
            }

            if (await _context.Users.AnyAsync(user => user.Username == pendingRegistration.Username))
            {
                TempData["AuthErrorMessage"] = "Ten tai khoan nay da duoc dang ky trong luc ban dang xac thuc OTP.";
                _context.PendingRegistrations.Remove(pendingRegistration);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Register));
            }

            if (await _context.Users.AnyAsync(user => user.Email == pendingRegistration.Email))
            {
                TempData["AuthErrorMessage"] = "Email nay da duoc dang ky trong luc ban dang xac thuc OTP.";
                _context.PendingRegistrations.Remove(pendingRegistration);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Register));
            }

            var user = new User
            {
                Username = pendingRegistration.Username,
                FullName = pendingRegistration.FullName,
                Email = pendingRegistration.Email,
                Password = _passwordSecurityService.EnsureHashedPassword(pendingRegistration.Password),
                RoleId = 2,
                IsEmailConfirmed = true
            };

            _context.Users.Add(user);
            _context.PendingRegistrations.Remove(pendingRegistration);
            await _context.SaveChangesAsync();

            TempData["AuthSuccessMessage"] = "Dang ky tai khoan thanh cong. Ban da co the dang nhap vao Zenpace.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendRegistrationOtp(string flowToken)
        {
            if (!_authFlowTokenService.TryReadRegistrationVerificationToken(flowToken, out var pendingRegistrationId))
            {
                TempData["AuthErrorMessage"] = "Lien ket xac thuc dang ky khong hop le hoac da het han.";
                return RedirectToAction(nameof(Register));
            }

            var pendingRegistration = await _context.PendingRegistrations.FindAsync(pendingRegistrationId);
            if (pendingRegistration == null)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay yeu cau dang ky can gui lai OTP.";
                return RedirectToAction(nameof(Register));
            }

            var otpResult = await IssueAndSendOtpAsync(pendingRegistration);
            TempData[otpResult.Success ? "AuthSuccessMessage" : "AuthErrorMessage"] = otpResult.Success
                ? "Da gui lai ma OTP moi toi email dang ky cua ban."
                : BuildOtpFailureMessage(otpResult.ErrorMessage);

            return RedirectToAction(nameof(VerifyRegistration), new { token = CreateRegistrationVerificationFlowToken(pendingRegistration.Id) });
        }

        [HttpGet]
        public async Task<IActionResult> VerifyEmail(string token)
        {
            if (!_authFlowTokenService.TryReadEmailVerificationToken(token, out var userId))
            {
                TempData["AuthErrorMessage"] = "Lien ket xac thuc email khong hop le hoac da het han.";
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay tai khoan can xac thuc.";
                return RedirectToAction(nameof(Login));
            }

            if (user.IsEmailConfirmed)
            {
                TempData["AuthSuccessMessage"] = "Email cua tai khoan nay da duoc xac thuc.";
                return RedirectToAction(nameof(Login));
            }

            return View(new VerifyEmailOtpViewModel
            {
                FlowToken = token,
                EmailMask = EmailVerificationService.MaskEmail(user.Email)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmail(VerifyEmailOtpViewModel model)
        {
            if (!_authFlowTokenService.TryReadEmailVerificationToken(model.FlowToken, out var userId))
            {
                TempData["AuthErrorMessage"] = "Lien ket xac thuc email khong hop le hoac da het han.";
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay tai khoan can xac thuc.";
                return RedirectToAction(nameof(Login));
            }

            model.EmailMask = EmailVerificationService.MaskEmail(user.Email);
            model.OtpCode = (model.OtpCode ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (user.IsEmailConfirmed)
            {
                TempData["AuthSuccessMessage"] = "Email cua tai khoan nay da duoc xac thuc.";
                return RedirectToAction(nameof(Login));
            }

            if (!_emailVerificationService.VerifyOtp(user, model.OtpCode))
            {
                ModelState.AddModelError(nameof(VerifyEmailOtpViewModel.OtpCode), "Ma OTP khong dung hoac da het han.");
                return View(model);
            }

            _emailVerificationService.MarkEmailConfirmed(user);
            await _context.SaveChangesAsync();

            TempData["AuthSuccessMessage"] = "Xac thuc email thanh cong. Ban da co the dang nhap vao Zenpace.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendEmailOtp(string flowToken)
        {
            if (!_authFlowTokenService.TryReadEmailVerificationToken(flowToken, out var userId))
            {
                TempData["AuthErrorMessage"] = "Lien ket xac thuc email khong hop le hoac da het han.";
                return RedirectToAction(nameof(Login));
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay tai khoan can gui lai OTP.";
                return RedirectToAction(nameof(Login));
            }

            if (user.IsEmailConfirmed)
            {
                TempData["AuthSuccessMessage"] = "Email cua tai khoan nay da duoc xac thuc.";
                return RedirectToAction(nameof(Login));
            }

            var otpResult = await IssueAndSendOtpAsync(user);
            TempData[otpResult.Success ? "AuthSuccessMessage" : "AuthErrorMessage"] = otpResult.Success
                ? "Da gui lai ma OTP moi toi email cua ban."
                : BuildOtpFailureMessage(otpResult.ErrorMessage);

            return RedirectToAction(nameof(VerifyEmail), new { token = CreateEmailVerificationFlowToken(user.Id) });
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            model.Identifier = model.Identifier?.Trim() ?? string.Empty;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await FindUserByIdentifierAsync(model.Identifier);
            if (user == null || !user.IsActive || !user.IsEmailConfirmed)
            {
                TempData["AuthSuccessMessage"] = "Neu thong tin hop le, he thong se gui ma OTP dat lai mat khau toi email da dang ky.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var otpResult = await IssueAndSendPasswordResetOtpAsync(user);
            if (!otpResult.Success)
            {
                TempData["AuthErrorMessage"] = BuildOtpFailureMessage(otpResult.ErrorMessage);
                return RedirectToAction(nameof(ForgotPassword));
            }

            TempData["AuthSuccessMessage"] = "Neu thong tin hop le, he thong da gui ma OTP dat lai mat khau toi email cua ban.";
            return RedirectToAction(nameof(ResetPassword), new { token = CreatePasswordResetFlowToken(user.Id) });
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token)
        {
            if (!_authFlowTokenService.TryReadPasswordResetToken(token, out var userId))
            {
                TempData["AuthErrorMessage"] = "Lien ket dat lai mat khau khong hop le hoac da het han.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.IsActive)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay tai khoan de dat lai mat khau.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            return View(new ResetPasswordViewModel
            {
                FlowToken = token,
                EmailMask = EmailVerificationService.MaskEmail(user.Email)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!_authFlowTokenService.TryReadPasswordResetToken(model.FlowToken, out var userId))
            {
                TempData["AuthErrorMessage"] = "Lien ket dat lai mat khau khong hop le hoac da het han.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.IsActive)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay tai khoan de dat lai mat khau.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            model.EmailMask = EmailVerificationService.MaskEmail(user.Email);
            model.OtpCode = (model.OtpCode ?? string.Empty).Trim();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!_emailVerificationService.VerifyPasswordResetOtp(user, model.OtpCode))
            {
                ModelState.AddModelError(nameof(ResetPasswordViewModel.OtpCode), "Ma OTP khong dung hoac da het han.");
                return View(model);
            }

            user.Password = _passwordSecurityService.HashPassword(model.NewPassword);
            _emailVerificationService.ClearPasswordReset(user);
            await _context.SaveChangesAsync();

            TempData["AuthSuccessMessage"] = "Dat lai mat khau thanh cong. Ban da co the dang nhap bang mat khau moi.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendPasswordResetOtp(string flowToken)
        {
            if (!_authFlowTokenService.TryReadPasswordResetToken(flowToken, out var userId))
            {
                TempData["AuthErrorMessage"] = "Lien ket dat lai mat khau khong hop le hoac da het han.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.IsActive)
            {
                TempData["AuthErrorMessage"] = "Khong tim thay tai khoan de gui lai OTP.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var otpResult = await IssueAndSendPasswordResetOtpAsync(user);
            TempData[otpResult.Success ? "AuthSuccessMessage" : "AuthErrorMessage"] = otpResult.Success
                ? "Da gui lai ma OTP dat lai mat khau."
                : BuildOtpFailureMessage(otpResult.ErrorMessage);

            return RedirectToAction(nameof(ResetPassword), new { token = CreatePasswordResetFlowToken(user.Id) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        private async Task<User?> FindUserByIdentifierAsync(string identifier)
        {
            return await _context.Users.FirstOrDefaultAsync(user =>
                user.Username == identifier || user.Email == identifier);
        }

        private async Task<PendingRegistration?> FindPendingRegistrationAsync(RegisterViewModel model)
        {
            return await _context.PendingRegistrations.FirstOrDefaultAsync(pendingRegistration =>
                pendingRegistration.OtpExpiresAt > DateTime.UtcNow
                && (pendingRegistration.Username == model.Username || pendingRegistration.Email == model.Email));
        }

        private async Task CleanupExpiredPendingRegistrationsAsync()
        {
            var expiredItems = await _context.PendingRegistrations
                .Where(pendingRegistration => pendingRegistration.OtpExpiresAt <= DateTime.UtcNow)
                .ToListAsync();

            if (expiredItems.Count == 0)
            {
                return;
            }

            _context.PendingRegistrations.RemoveRange(expiredItems);
            await _context.SaveChangesAsync();
        }

        private async Task<(bool Success, string? ErrorMessage)> IssueAndSendOtpAsync(User user)
        {
            if (!_emailVerificationService.CanIssueOtp(user, out var retryAfter))
            {
                return (false, BuildOtpCooldownMessage(retryAfter));
            }

            try
            {
                var otpCode = _emailVerificationService.IssueOtp(user);
                await _context.SaveChangesAsync();
                await _emailVerificationService.SendOtpEmailAsync(user, otpCode, HttpContext.RequestAborted);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> IssueAndSendOtpAsync(PendingRegistration pendingRegistration)
        {
            if (!_emailVerificationService.CanIssueOtp(pendingRegistration, out var retryAfter))
            {
                return (false, BuildOtpCooldownMessage(retryAfter));
            }

            try
            {
                var otpCode = _emailVerificationService.IssueOtp(pendingRegistration);
                await _context.SaveChangesAsync();
                await _emailVerificationService.SendOtpEmailAsync(pendingRegistration, otpCode, HttpContext.RequestAborted);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string? ErrorMessage)> IssueAndSendPasswordResetOtpAsync(User user)
        {
            if (!_emailVerificationService.CanIssuePasswordResetOtp(user, out var retryAfter))
            {
                return (false, BuildOtpCooldownMessage(retryAfter));
            }

            try
            {
                var otpCode = _emailVerificationService.IssuePasswordResetOtp(user);
                await _context.SaveChangesAsync();
                await _emailVerificationService.SendPasswordResetOtpEmailAsync(user, otpCode, HttpContext.RequestAborted);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private string CreateEmailVerificationFlowToken(int userId)
        {
            return _authFlowTokenService.CreateEmailVerificationToken(userId, EmailVerificationService.GetFlowTokenLifetime());
        }

        private string CreateRegistrationVerificationFlowToken(int pendingRegistrationId)
        {
            return _authFlowTokenService.CreateRegistrationVerificationToken(pendingRegistrationId, EmailVerificationService.GetFlowTokenLifetime());
        }

        private string CreatePasswordResetFlowToken(int userId)
        {
            return _authFlowTokenService.CreatePasswordResetToken(userId, EmailVerificationService.GetFlowTokenLifetime());
        }

        private static string BuildOtpFailureMessage(string? detail)
        {
            return string.IsNullOrWhiteSpace(detail)
                ? "He thong chua gui duoc OTP luc nay."
                : $"He thong chua gui duoc OTP: {detail}";
        }

        private static string BuildOtpCooldownMessage(TimeSpan retryAfter)
        {
            var seconds = Math.Max((int)Math.Ceiling(retryAfter.TotalSeconds), 1);
            return $"Vui long cho {seconds} giay truoc khi yeu cau ma OTP moi.";
        }
    }
}
