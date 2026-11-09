namespace Vroo.Models;

/// <summary>A make/model the user wants tracked on bytbil.com.</summary>
public class WatchedModel
{
    public int Id { get; set; }
    /// <summary>As bytbil spells it in the Makes parameter, e.g. "Tesla", "Mercedes-Benz".</summary>
    public required string Make { get; set; }
    /// <summary>As bytbil spells it in the Models parameter, e.g. "Model Y", "S-Klass".</summary>
    public required string Model { get; set; }
    /// <summary>Paused models are kept with their history but skipped by the daily run.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public List<Car> Cars { get; set; } = [];
    public List<ScrapeRun> Runs { get; set; } = [];
    public List<DailyStat> DailyStats { get; set; } = [];
}

/// <summary>
/// A small picture of the model for the overview, copied from one listing so it outlives the ad.
/// Its own table so model lists don't load the bytes.
/// </summary>
public class ModelImage
{
    public int WatchedModelId { get; set; }
    public WatchedModel WatchedModel { get; set; } = null!;
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
}

/// <summary>One listing on bytbil. Identified by URL, or by registration number when relisted.</summary>
public class Car
{
    public int Id { get; set; }
    public int WatchedModelId { get; set; }
    public WatchedModel WatchedModel { get; set; } = null!;

    public required string Url { get; set; }
    public required string Title { get; set; }
    public string? RegistrationNumber { get; set; }
    public int? Year { get; set; }
    /// <summary>Swedish mil (10 km), as bytbil shows it.</summary>
    public int? MileageMil { get; set; }
    public string? Location { get; set; }
    /// <summary>Current asking price in SEK. A cache of the latest <see cref="CarPrice"/>, written alongside it.</summary>
    public int Price { get; set; }
    public string? ImageUrl { get; set; }
    public string? Color { get; set; }
    public string? DriveType { get; set; }
    public string? Gearbox { get; set; }
    public string? BodyType { get; set; }
    /// <summary>When the listing page (regnr, specs) was fetched; null until a fetch succeeds.</summary>
    public DateTime? DetailsFetchedAt { get; set; }
    /// <summary>Failed listing-page fetches. Later runs retry until this reaches the limit in ScrapeService.</summary>
    public int DetailAttempts { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public List<CarPrice> Prices { get; set; } = [];
    public List<Sighting> Sightings { get; set; } = [];
}

/// <summary>
/// The car was for sale on this day (Stockholm date). Presence only: the price that day
/// comes from <see cref="CarPrice"/>, which is the single source for prices.
/// </summary>
public class Sighting
{
    public int CarId { get; set; }
    public Car Car { get; set; } = null!;
    public DateOnly Date { get; set; }
}

/// <summary>An asking price and when it was first observed. Written on first sighting and on every change.</summary>
public class CarPrice
{
    public long Id { get; set; }
    public int CarId { get; set; }
    public Car Car { get; set; } = null!;
    public int Price { get; set; }
    public DateTime ObservedAt { get; set; }
}

/// <summary>One scrape of one watched model.</summary>
public class ScrapeRun
{
    public int Id { get; set; }
    public int WatchedModelId { get; set; }
    public WatchedModel WatchedModel { get; set; } = null!;
    /// <summary>"schedule", "manual" or "import".</summary>
    public required string Trigger { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int CarsSeen { get; set; }
    public int NewCars { get; set; }
    public int PriceChanges { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Market snapshot of a watched model on one day. A cache computed from Sightings + CarPrices
/// at the end of each successful run; it can be recomputed from them at any time.
/// </summary>
public class DailyStat
{
    public int WatchedModelId { get; set; }
    public WatchedModel WatchedModel { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int Listings { get; set; }
    public int MedianPrice { get; set; }
    public int? MedianMileageMil { get; set; }
}
