namespace Vroo.Models.DTOs;

/// <summary>Login with the single admin password.</summary>
public record LoginRequest(string Password);

/// <summary>JWT for the SPA.</summary>
public record LoginResponse(string Token, DateTime ExpiresAt);

/// <summary>Start watching a make/model, spelled as on bytbil.</summary>
public record WatchedModelCreateRequest(string Make, string Model);

/// <summary>Pause or resume the daily scrape for a model.</summary>
public record WatchedModelUpdateRequest(bool IsActive);

/// <summary>Outcome of one scrape.</summary>
public record ScrapeRunResponse(
    int Id, int WatchedModelId, string Make, string Model, string Trigger,
    DateTime StartedAt, DateTime? FinishedAt, int CarsSeen, int NewCars, int PriceChanges, string? Error);

/// <summary>A watched model with its latest market numbers.</summary>
public record WatchedModelResponse(
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
