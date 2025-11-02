using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using SurveyWeb.Models;

namespace SurveyWeb.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(SurveyDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        await EnsureRolesAsync(db);
        var admin = await EnsureUserAsync(
            db,
            email: "admin@surveyweb.local",
            displayName: "Quản trị viên",
            password: "Admin@123");

        var user = await EnsureUserAsync(
            db,
            email: "user@surveyweb.local",
            displayName: "Người dùng mẫu",
            password: "User@123");

        await EnsureUserRoleAsync(db, admin, "Admin");
        await EnsureUserRoleAsync(db, user, "User");
    }

    private static async Task EnsureRolesAsync(SurveyDbContext db)
    {
        var now = DateTime.UtcNow;
        var roles = new[]
        {
            new Role { Name = "Admin", Description = "Quản trị hệ thống", CreatedAt = now },
            new Role { Name = "User", Description = "Người dùng tiêu chuẩn", CreatedAt = now }
        };

        foreach (var role in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == role.Name))
            {
                role.CreatedAt = DateTime.UtcNow;
                db.Roles.Add(role);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task<User> EnsureUserAsync(SurveyDbContext db, string email, string displayName, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null) return user;

        user = new User
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = HashPassword(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    private static async Task EnsureUserRoleAsync(SurveyDbContext db, User user, string roleName)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role == null) return;

        var exists = await db.UserRoles
            .AnyAsync(ur => ur.UserId == user.UserId && ur.RoleId == role.RoleId);

        if (exists) return;

        db.UserRoles.Add(new UserRole
        {
            UserId = user.UserId,
            RoleId = role.RoleId,
            GrantedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static string HashPassword(string password, int iterations = 100_000)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, 32);
        return $"pbkdf2|it={iterations}|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(key)}";
    }
}
