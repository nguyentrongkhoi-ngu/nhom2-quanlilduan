using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SurveyWeb.Data;
using SurveyWeb.Models;

namespace SurveyWeb.Areas.Public.Controllers;

[Area("Public")]
public class AccountController : Controller
{
    private readonly SurveyDbContext _db;
    public AccountController(SurveyDbContext db) => _db = db;

    // ===== ViewModels =====
    public class RegisterVM
    {
        [Display(Name = "Họ và tên *")]
        [Required, StringLength(100)]
        public string FullName { get; set; } = "";

        [Display(Name = "Email *")]
        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = "";

        [Display(Name = "Số điện thoại")]
        [Phone, StringLength(20)]
        public string? Phone { get; set; }

        [Display(Name = "Mật khẩu *")]
        [Required, MinLength(6)]
        public string Password { get; set; } = "";

        [Display(Name = "Xác nhận mật khẩu *")]
        [Required, Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp")]
        public string ConfirmPassword { get; set; } = "";

        [Range(typeof(bool), "true", "true", ErrorMessage = "Vui lòng đồng ý điều khoản sử dụng")]
        public bool Terms { get; set; }

        public bool Newsletter { get; set; }
    }

    public class LoginVM
    {
        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = "";
        [Required]
        public string Password { get; set; } = "";
        public bool Remember { get; set; }
        public string? ReturnUrl { get; set; }
    }

    // ===== Login (GET) =====
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User?.Identity?.IsAuthenticated ?? false)
            return RedirectToAction("Index", "Home");
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    // ===== Register (GET) =====
    [HttpGet]
    public IActionResult Register()
    {
        if (User?.Identity?.IsAuthenticated ?? false)
            return RedirectToAction("Index", "Home");
        return View(new RegisterVM());
    }

    // ===== Register (POST) =====
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == vm.Email))
        {
            ModelState.AddModelError(nameof(vm.Email), "Email đã tồn tại");
            return View(vm);
        }

        var (hash, salt, iter) = HashPassword(vm.Password);
        var user = new User
        {
            Email = vm.Email.Trim(),
            PasswordHash = $"pbkdf2|it={iter}|{salt}|{hash}",
            DisplayName = vm.FullName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        var userRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "User");
        if (userRole != null)
        {
            _db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = userRole.RoleId,
                GrantedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        ViewBag.Success = true;
        ViewBag.RegisteredEmail = user.Email;
        return View(new RegisterVM());
    }

    // ===== Login (POST) =====
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == vm.Email && u.IsActive);
        if (user == null || !VerifyPassword(vm.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng");
            return View(vm);
        }

        // Lấy role names của user
        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == user.UserId)
            .Include(ur => ur.Role)
            .Select(ur => ur.Role!.Name)
            .ToListAsync();

        // Claims
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!),
            new Claim(ClaimTypes.Email, user.Email)
        };
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r)); 

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var props = new AuthenticationProperties
        {
            IsPersistent = vm.Remember,
            ExpiresUtc = vm.Remember ? DateTimeOffset.UtcNow.AddDays(30) : null,
            RedirectUri = vm.ReturnUrl
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            return Redirect(vm.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    // ===== Logout =====
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // ===== Password Utils =====
    private static (string hashB64, string saltB64, int iterations) HashPassword(string password, int iterations = 100_000)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key  = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, 32);
        return (Convert.ToBase64String(key), Convert.ToBase64String(salt), iterations);
    }

    private static bool VerifyPassword(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return false;
        var parts = stored.Split('|');
        if (parts.Length != 4 || !parts[0].Equals("pbkdf2", StringComparison.OrdinalIgnoreCase)) return false;

        var itStr = parts[1].StartsWith("it=") ? parts[1][3..] : "100000";
        if (!int.TryParse(itStr, out var iterations)) iterations = 100_000;

        byte[] salt, expected;
        try { salt = Convert.FromBase64String(parts[2]); expected = Convert.FromBase64String(parts[3]); }
        catch { return false; }

        var calc = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(calc, expected);
    }
}

