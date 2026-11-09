using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Vroo.Services;

public record ListingSummary(
    string Url, string Title, int Price, int? Year, int? MileageMil, string? Location, string? ImageUrl);

public record ListingDetails(
    string? RegistrationNumber, string? Color, string? DriveType, string? Gearbox, string? BodyType);

// Scrapes bytbil.com search results and listing pages. Ported from usedcarprices/main.py.
// Every request is preceded by a human-like pause, so a caller never needs to add its own.
public partial class BytbilClient(HttpClient http, ILogger<BytbilClient> logger)
{
    public const string BaseUrl = "https://www.bytbil.com";
    private readonly HtmlParser _parser = new();

    /// <summary>One page of results, or null if the request failed. An empty list means past the last page.</summary>
    public async Task<List<ListingSummary>?> GetSearchPageAsync(string make, string model, int page, CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["Makes[0]"] = make,
            ["Models[0]"] = model,
            ["OnlyNew"] = "False",
            ["OnlyWarrantyProgram"] = "False",
            ["OnlyEnvironmentFriendly"] = "False",
            ["OnlyFourWheelDrive"] = "False",
            ["OnlyReducedPrice"] = "False",
            ["OnlyDeductibleVAT"] = "False",
            ["OnlyIsAuction"] = "False",
            ["OnlyAuthorizedDealers"] = "False",
            ["OnlyHasImage"] = "False",
            ["OnlyHasVideo"] = "False",
            ["OnlyNoBonusMalus"] = "False",
            ["ShowLeasingOffers"] = "False",
            ["OnlyHasCarfaxReport"] = "False",
            ["Page"] = page.ToString(),
            ["IgnoreSortFiltering"] = "False",
        };
        var url = $"{BaseUrl}/bil?" + string.Join("&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var html = await GetHtmlAsync(url, ct);
        return html is null ? null : ParseSearchPage(html);
    }

    public async Task<ListingDetails?> GetDetailsAsync(string url, CancellationToken ct)
    {
        var html = await GetHtmlAsync(url, ct);
        if (html is null) return null;

        var doc = _parser.ParseDocument(html);
        string? Field(string label) => doc.QuerySelectorAll("dt")
            .FirstOrDefault(dt => dt.TextContent.Trim() == label)?
            .NextElementSibling is { LocalName: "dd" } dd ? NullIfEmpty(dd.TextContent.Trim()) : null;

        var regnr = Field("Regnr");
        if (regnr is "-" or "N/A") regnr = null;

        return new ListingDetails(regnr, Field("Färg"), Field("Drivhjul"), Field("Växellåda"), Field("Karosseri"));
    }

    internal List<ListingSummary> ParseSearchPage(string html)
    {
        var doc = _parser.ParseDocument(html);
        var result = new List<ListingSummary>();

        foreach (var item in doc.QuerySelectorAll("ul.result-list li.result-list-item"))
        {
            var priceText = item.QuerySelector("span.car-price-main")?.TextContent;
            if (priceText is null) continue;
            if (priceText.Contains("/mån", StringComparison.OrdinalIgnoreCase)) continue; // leasing offer
            var price = ParseInt(priceText);
            if (price is null) continue;

            var link = item.QuerySelector("h3.car-list-header a");
            var href = link?.GetAttribute("href");
            if (link is null || string.IsNullOrEmpty(href)) continue;

            var details = item.QuerySelector("p.uk-text-truncate");
            if (details is null) continue;
            var parts = details.TextContent.Split('|').Select(p => p.Trim()).ToArray();

            var image = item.QuerySelector(".car-image")?.GetAttribute("style") is { } style
                ? ImageUrlRegex().Match(style) is { Success: true } m ? m.Groups[1].Value : null
                : null;

            result.Add(new ListingSummary(
                Url: new Uri(new Uri(BaseUrl), href).ToString(),
                Title: link.TextContent.Trim(),
                Price: price.Value,
                Year: parts.Length > 0 ? ParseInt(parts[0]) : null,
                MileageMil: parts.Length > 1 ? ParseInt(parts[1]) : null,
                Location: parts.Length > 2 ? NullIfEmpty(parts[2]) : null,
                ImageUrl: image));
        }

        return result;
    }

    /// <summary>A listing's thumbnail (about 320×213, 15 KB), or null if it couldn't be fetched.</summary>
    public async Task<(byte[] Data, string ContentType)?> GetImageAsync(string url, CancellationToken ct)
    {
        await HumanLikeDelayAsync(ct);
        try
        {
            using var response = await http.GetAsync(url, ct);
            var type = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || type is null || !type.StartsWith("image/"))
            {
                logger.LogWarning("Image request returned {Status} {Type} for {Url}", (int)response.StatusCode, type, url);
                return null;
            }
            var data = await response.Content.ReadAsByteArrayAsync(ct);
            return data.Length is > 0 and <= 1_000_000 ? (data, type) : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Image request failed: {Url}", url);
            return null;
        }
    }

    private async Task<string?> GetHtmlAsync(string url, CancellationToken ct)
    {
        await HumanLikeDelayAsync(ct);
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("bytbil returned {Status} for {Url}", (int)response.StatusCode, url);
                return null;
            }
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Request to bytbil failed: {Url}", url);
            return null;
        }
    }

    // Be nice to the server: 2–5 s between requests, occasionally 5–9 s.
    private static Task HumanLikeDelayAsync(CancellationToken ct)
    {
        var seconds = Random.Shared.NextDouble() < 0.1
            ? 5 + Random.Shared.NextDouble() * 4
            : 2 + Random.Shared.NextDouble() * 3;
        seconds += Random.Shared.NextDouble() * 0.5;
        return Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    // "479 800 kr", "4 236 mil", "2024" → digits only.
    internal static int? ParseInt(string text)
    {
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 9 ? int.Parse(digits) : null;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    [GeneratedRegex(@"url\(([^)]+)\)")]
    private static partial Regex ImageUrlRegex();
}
