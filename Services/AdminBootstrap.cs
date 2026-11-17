using Microsoft.EntityFrameworkCore;
using Vroo.Data;
using Vroo.Endpoints;
using Vroo.Models;

namespace Vroo.Services;

// Makes sure the admin from configuration (Auth:AdminEmail + Auth:Password) exists. Runs at startup.
// The AddUsers migration left a placeholder admin (empty email) owning the models from the
// single-user era; the first run gives it the configured email and password.
public static class AdminBootstrap
{
    public static async Task RunAsync(VrooDbContext db, IConfiguration config, ILogger logger)
    {
        var email = AuthEndpoints.Normalize(config["Auth:AdminEmail"]);
        var password = config["Auth:Password"];
        if (email.Length == 0 || string.IsNullOrEmpty(password))
        {
            logger.LogWarning("Auth:AdminEmail or Auth:Password not set; no admin account is created");
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null)
        {
            // The password in config only seeds the account; after that the admin changes it like anyone.
            if (!user.IsAdmin)
            {
                user.IsAdmin = true;
                await db.SaveChangesAsync();
                logger.LogInformation("Made {Email} admin", email);
            }
            return;
        }

        var now = DateTime.UtcNow;
        user = await db.Users.FirstOrDefaultAsync(u => u.Email == "")
            ?? db.Users.Add(new User { Email = "", PasswordHash = "", CreatedAt = now }).Entity;
        user.Email = email;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.IsAdmin = true;
        user.EmailVerifiedAt = now;
        user.PasswordChangedAt = now;
        await db.SaveChangesAsync();
        logger.LogInformation("Created admin account {Email}", email);
    }
}
