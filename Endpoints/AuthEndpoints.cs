using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Vroo.Models.DTOs;

namespace Vroo.Endpoints;

// Single-user login: the password lives in configuration (Auth:Password, from .env.vroo).
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", (LoginRequest req, IConfiguration config, ILoggerFactory lf, HttpContext http) =>
        {
            var expected = config["Auth:Password"];
            if (string.IsNullOrEmpty(expected))
                return Results.Problem("Auth:Password is not configured");

            var ok = CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(req.Password ?? "")),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
            if (!ok)
            {
                lf.CreateLogger("Security").LogWarning("Failed login from {IP}", http.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            var expires = DateTime.UtcNow.AddDays(30);
            var token = new JwtSecurityToken(
                issuer: "vroo",
                audience: "vroo",
                claims: [new("sub", "admin")],
                expires: expires,
                signingCredentials: new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256));

            return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires));
        })
        .RequireRateLimiting("login");
    }

    public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Convert.FromBase64String(config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured")));
}
