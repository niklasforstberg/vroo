namespace Vroo.Models.DTOs;

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Email, string Password);

/// <summary>Token and email from a verification or reset link.</summary>
public record TokenRequest(string Email, string Token);

public record EmailRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

/// <summary>The logged-in user.</summary>
public record MeResponse(string Email, bool IsAdmin);

/// <summary>JWT for the SPA.</summary>
public record LoginResponse(string Token, DateTime ExpiresAt);

/// <summary>Start watching a make/model, spelled as on bytbil.</summary>
public record WatchedModelCreateRequest(string Make, string Model);

/// <summary>Pause or resume the user's subscription to a model.</summary>
public record WatchedModelUpdateRequest(bool IsActive);

/// <summary>Outcome of one scrape.</summary>
public record ScrapeRunResponse(
    int Id, int WatchedModelId, string Make, string Model, string Trigger,
    DateTime StartedAt, DateTime? FinishedAt, int CarsSeen, int NewCars, int PriceChanges, string? Error);

/// <summary>A watched model with its latest market numbers.</summary>
public record WatchedModelResponse(
    // IsActive: the current user's subscription is active (false if they don't follow it).
    int Id, string Make, string Model, bool IsActive, bool HasImage,
    // "idle", "queued" or "running".
    string Status,
    int? Listings, int? MedianPrice, DateOnly? StatDate,
    // Median price 30–45 days before StatDate, for the trend; null if there is no such point.
    int? MedianPrice30DaysAgo,
    ScrapeRunResponse? LastRun);

/// <summary>One day of market data for the trend chart.</summary>
public record DailyStatResponse(DateOnly Date, int Listings, int MedianPrice, int? MedianMileageMil);

/// <summary>Model detail: summary plus its full daily series.</summary>
public record WatchedModelDetailResponse(WatchedModelResponse Model, List<DailyStatResponse> Stats);

/// <summary>A listing in the model's car table.</summary>
public record CarResponse(
    int Id, string Title, string Url, string? ImageUrl, string? RegistrationNumber,
    int? Year, int? MileageMil, string? Location, string? Gearbox, string? DriveType,
    int Price, int FirstPrice, int PriceChanges,
    DateTime FirstSeenAt, DateTime LastSeenAt,
    // Seen in the model's most recent successful run.
    bool IsListed,
    // False for leasing amounts and placeholders posted as a price; left out of stats.
    bool PlausiblePrice);

/// <summary>A price observation.</summary>
public record CarPriceResponse(int Price, DateTime ObservedAt);

/// <summary>A listing with its full price history.</summary>
public record CarDetailResponse(CarResponse Car, string? Color, string? BodyType, List<CarPriceResponse> Prices);

/// <summary>A make/model someone already watches, offered as a suggestion when adding.</summary>
public record CatalogEntry(string Make, string Model);

/// <summary>A user in the admin list.</summary>
public record AdminUserResponse(
    int Id, string Email, bool IsAdmin, DateTime CreatedAt, DateTime? LastSeenAt, bool Verified, int Models);

/// <summary>A watched model in the admin list, with how many follow it.</summary>
public record AdminModelResponse(int Id, string Make, string Model, int Followers, int ActiveFollowers, int Cars);
