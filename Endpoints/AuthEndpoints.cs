using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Vroo.Data;
using Vroo.Models;
using Vroo.Models.DTOs;
using Vroo.Services;

namespace Vroo.Endpoints;

// Email + password accounts with open signup. Email must be verified before login;
// forgotten passwords are reset through an emailed link.
public static class AuthEndpoints
{
    private const int MinPasswordLength = 8;
    // Verified against when the email is unknown, so response time doesn't reveal which addresses exist.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password");

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapPost("/login", async (LoginRequest req, VrooDbContext db, IConfiguration config, ILoggerFactory lf, HttpContext http) =>
        {
            var log = lf.CreateLogger("Security");
            var email = Normalize(req.Email);
            var user = email.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || user.PasswordHash.Length == 0)
            {
                BCrypt.Net.BCrypt.Verify(req.Password ?? "", DummyHash);
                log.LogWarning("Failed login: unknown email {Email} from {IP}", email, http.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }
            if (!BCrypt.Net.BCrypt.Verify(req.Password ?? "", user.PasswordHash))
            {
                log.LogWarning("Failed login: wrong password for {Email} from {IP}", email, http.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }
            if (user.EmailVerifiedAt is null)
                return Results.Json(new { error = "email_not_verified" }, statusCode: StatusCodes.Status403Forbidden);

            user.LastSeenAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(IssueJwt(user, config));
        });

        auth.MapPost("/register", async (RegisterRequest req, VrooDbContext db, IConfiguration config, EmailService email, ILoggerFactory lf) =>
        {
            var address = Normalize(req.Email);
            if (address.Length > 254 || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
                return Results.BadRequest(new { error = "Ange en giltig e-postadress." });
            if ((req.Password ?? "").Length < MinPasswordLength)
                return Results.BadRequest(new { error = $"Lösenordet måste vara minst {MinPasswordLength} tecken." });

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address);
            if (user?.EmailVerifiedAt is not null)
                return Results.Conflict(new { error = "Det finns redan ett konto med den e-postadressen." });

            // Signing up again on an unverified address: neither the stored password (maybe a stranger's,
            // registered to pre-hijack the account) nor this one can be trusted. Send a choose-password
            // link instead, so the password is set by whoever controls the mailbox.
            if (user is not null)
            {
                var resetToken = NewToken(out var resetHash);
                user.ResetTokenHash = resetHash;
                user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1);
                await db.SaveChangesAsync();
                await TrySendAsync(lf, address, () => email.SendPasswordResetAsync(address, Link(config, "reset-password", resetToken, address)));
                return Results.Ok();
            }

            var now = DateTime.UtcNow;
            user = new User
            {
                Email = address, PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                CreatedAt = now, PasswordChangedAt = now,
            };
            db.Users.Add(user);

            var token = NewToken(out var hash);
            user.VerificationTokenHash = hash;
            user.VerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
            await db.SaveChangesAsync();

            await TrySendAsync(lf, address, () => email.SendVerificationAsync(address, Link(config, "verify-email", token, address)));
            return Results.Ok();
        });

        // Doesn't log in: the link proves the mailbox, not that the clicker chose the password.
        // Logging in afterwards proves that, so nobody ends up using an account with a stranger's password.
        auth.MapPost("/verify-email", async (TokenRequest req, VrooDbContext db) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == Normalize(req.Email));
            if (user is null) return Results.BadRequest(new { error = "Länken är ogiltig." });
            if (user.EmailVerifiedAt is not null) return Results.Ok(new { alreadyVerified = true });
            if (!Matches(req.Token, user.VerificationTokenHash, user.VerificationTokenExpiry))
                return Results.BadRequest(new { error = "Länken är ogiltig eller har gått ut." });

            user.EmailVerifiedAt = DateTime.UtcNow;
            user.VerificationTokenHash = null;
            user.VerificationTokenExpiry = null;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        auth.MapPost("/resend-verification", async (EmailRequest req, VrooDbContext db, IConfiguration config, EmailService email, ILoggerFactory lf) =>
        {
            // Same answer whether or not the address exists.
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == Normalize(req.Email));
            if (user is null || user.EmailVerifiedAt is not null) return Results.Ok();

            var token = NewToken(out var hash);
            user.VerificationTokenHash = hash;
            user.VerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
            await db.SaveChangesAsync();
            await TrySendAsync(lf, user.Email, () => email.SendVerificationAsync(user.Email, Link(config, "verify-email", token, user.Email)));
            return Results.Ok();
        });

        auth.MapPost("/forgot-password", async (EmailRequest req, VrooDbContext db, IConfiguration config, EmailService email, ILoggerFactory lf) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == Normalize(req.Email));
            if (user is null) return Results.Ok();

            var token = NewToken(out var hash);
            user.ResetTokenHash = hash;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
            await TrySendAsync(lf, user.Email, () => email.SendPasswordResetAsync(user.Email, Link(config, "reset-password", token, user.Email)));
            return Results.Ok();
        });

        auth.MapPost("/reset-password", async (ResetPasswordRequest req, VrooDbContext db, IConfiguration config) =>
        {
            if ((req.NewPassword ?? "").Length < MinPasswordLength)
                return Results.BadRequest(new { error = $"Lösenordet måste vara minst {MinPasswordLength} tecken." });
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == Normalize(req.Email));
            if (user is null || !Matches(req.Token, user.ResetTokenHash, user.ResetTokenExpiry))
                return Results.BadRequest(new { error = "Länken är ogiltig eller har gått ut." });

            var now = DateTime.UtcNow;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            user.PasswordChangedAt = now;
            user.ResetTokenHash = null;
            user.ResetTokenExpiry = null;
            // The link reached the mailbox, which proves the address too.
            user.EmailVerifiedAt ??= now;
            user.VerificationTokenHash = null;
            user.VerificationTokenExpiry = null;
            user.LastSeenAt = now;
            await db.SaveChangesAsync();
            return Results.Ok(IssueJwt(user, config));
        });

        app.MapGet("/api/me", async (ClaimsPrincipal principal, VrooDbContext db) =>
        {
            var id = principal.UserId();
            return await db.Users.Where(u => u.Id == id).Select(u => new MeResponse(u.Email, u.IsAdmin)).FirstAsync();
        }).RequireAuthorization();
    }

    /// <summary>
    /// Runs on every authenticated request: rejects tokens of deleted users or issued before the
    /// latest password change, and adds the admin role from the database (so it can be revoked).
    /// </summary>
    public static async Task ValidateUserAsync(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal!;
        var db = ctx.HttpContext.RequestServices.GetRequiredService<VrooDbContext>();
        if (!int.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            || !long.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Iat), out var iat))
        {
            ctx.Fail("Bad token");
            return;
        }
        var user = await db.Users.Where(u => u.Id == id)
            .Select(u => new { u.IsAdmin, u.PasswordChangedAt }).FirstOrDefaultAsync();
        // iat has whole seconds; PasswordChangedAt is truncated to match.
        if (user is null || iat < new DateTimeOffset(user.PasswordChangedAt).ToUnixTimeSeconds())
        {
            ctx.Fail("User gone or password changed");
            return;
        }
        if (user.IsAdmin) ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "admin"));
    }

    public static int UserId(this ClaimsPrincipal p) => int.Parse(p.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Convert.FromBase64String(config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured")));

    private static LoginResponse IssueJwt(User user, IConfiguration config)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddDays(30).UtcDateTime;
        var token = new JwtSecurityToken(
            issuer: "vroo",
            audience: "vroo",
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    // 256 random bits; a plain SHA-256 is enough to store it (no need for a slow hash).
    private static string NewToken(out string hash)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        hash = Sha256(token);
        return token;
    }

    private static bool Matches(string? token, string? storedHash, DateTime? expiry) =>
        !string.IsNullOrEmpty(token) && storedHash is not null && expiry > DateTime.UtcNow
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sha256(token)), Encoding.ASCII.GetBytes(storedHash));

    private static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static string Link(IConfiguration config, string page, string token, string email) =>
        $"{(config["AppUrl"] ?? "").TrimEnd('/')}/{page}?token={token}&email={Uri.EscapeDataString(email)}";

    // A failing mail server must not fail the request; the user can ask for a new link.
    private static async Task TrySendAsync(ILoggerFactory lf, string to, Func<Task> send)
    {
        try { await send(); }
        catch (Exception ex) { lf.CreateLogger("Email").LogError(ex, "Failed to send email to {To}", to); }
    }
}
