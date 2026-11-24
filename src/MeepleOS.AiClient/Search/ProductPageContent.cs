using System.Net;
using System.Text.RegularExpressions;

namespace MeepleOS.AiClient;

internal static class ProductPageContent
{
    private static readonly Regex PlayerSpecification = new(
        @"^(?:(?:aantal\s+spelers|spelers|players|number\s+of\s+players)\s*[:|]?\s*)?(?<min>\d+)\s*(?:-|–|—|to|tot|t/m)\s*(?<max>\d+)\s*(?:spelers|players)?\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DurationSpecification = new(
        @"^(?:(?:speelduur|speeltijd|playing\s*time|play\s*time|duration)\s*[:|]?\s*)?(?:(?:\+/-|±|ca\.?|ongeveer|about)\s*)?(?<minutes>\d+)\s*(?:min\.?|minuten|minutes?)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Relevant = new(
        @"\b(?:spelers?|players?|speelduur|speeltijd|playing\s*time|play\s*time|duration|productinformatie|specificaties|specifications)\b|\b\d+\s*(?:minuten|minutes|min\.?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static GameSearchHit Enrich(GameSearchHit hit, string rawContent)
    {
        var lines = WebUtility.HtmlDecode(rawContent).Replace('\u00a0', ' ').Replace('\u202f', ' ')
            .Split('\n').Select(line => Regex.Replace(line, @"\s+", " ").Trim(' ', '\r', '*', '|'))
            .Where(line => line.Length > 0).ToArray();
        var players = lines.Select(line => PlayerSpecification.Match(line)).FirstOrDefault(match => match.Success);
        var duration = lines.Select(line => DurationSpecification.Match(line)).FirstOrDefault(match => match.Success);
        var facts = new List<string>();
        if (players is { Success: true })
            facts.Add($"Spelers: {players.Groups["min"].Value}-{players.Groups["max"].Value}.");
        if (duration is { Success: true })
            facts.Add($"Speelduur: {duration.Groups["minutes"].Value} minuten.");

        var selected = lines.Where(line => line.StartsWith('#') || Relevant.IsMatch(line))
            .Select(line => line.Length > 700 ? line[..700] : line).ToArray();
        var pageText = string.Join("\n", selected);
        if (pageText.Length < 200) pageText = string.Join("\n", lines);
        if (pageText.Length > 7500) pageText = pageText[..7500];
        return hit with
        {
            Content = $"Productpagina:\n{pageText}\nZoekfragment: {hit.Content}",
            ProductFacts = facts.Count == 0 ? null : string.Join(" ", facts)
        };
    }
}
