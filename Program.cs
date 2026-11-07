using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Vroo.Data;
using Vroo.Endpoints;
using Vroo.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, config) => config
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .WriteTo.Console());

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<VrooDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string not found")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "vroo",
            ValidAudience = "vroo",
            IssuerSigningKey = AuthEndpoints.SigningKey(builder.Configuration),
        };
    });
builder.Services.AddAuthorization();

// Behind Caddy + Cloudflare: take the visitor's IP from the forwarded header so the
// login rate limit is per visitor, not per proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("http://localhost:5173").AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddHttpClient<BytbilClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("sv-SE,sv;q=0.9,en;q=0.8");
    c.DefaultRequestHeaders.Referrer = new Uri("https://www.bytbil.com/");
});
builder.Services.AddScoped<ScrapeService>();
builder.Services.AddSingleton<ScrapeScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScrapeScheduler>());

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VrooDbContext>();
    db.Database.Migrate();

    // One-off: `dotnet Vroo.dll import /path/to/cars.db`
    if (args is ["import", var path])
    {
        await SqliteImporter.RunAsync(db, path, app.Logger);
        return;
    }
}

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment()) app.UseCors("dev");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapWatchedModelEndpoints();
app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

app.Run();
