using System.Text.Json;

namespace MeepleOS.AiClient;

public sealed class RequestLedger(string path, int perMinute, int perDay, Func<DateTimeOffset>? clock = null)
{
    public const int DefaultPerMinute = 15;
    public const int DefaultPerDay = 500;
    private readonly List<DateTimeOffset> requests = Load(path);

    public void Reserve()
    {
        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;
        requests.RemoveAll(t => t < now.AddDays(-1));
        if (requests.Count(t => t > now.AddMinutes(-1)) >= perMinute)
            throw new InvalidOperationException($"Lokale Gemini-limiet: {perMinute} verzoeken per minuut. Probeer straks opnieuw.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        if (requests.Count(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zone).DateTime) == day) >= perDay)
            throw new InvalidOperationException($"Lokale Gemini-limiet: {perDay} verzoeken per dag. Probeer morgen opnieuw.");
        requests.Add(now);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(requests));
    }

    private static List<DateTimeOffset> Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<DateTimeOffset>>(File.ReadAllText(path)) ?? [] : []; }
        catch (JsonException) { throw new InvalidOperationException("Lokaal Gemini-verzoekenlog is ongeldig; herstel het bestand vóór nieuwe verzoeken."); }
    }
}
