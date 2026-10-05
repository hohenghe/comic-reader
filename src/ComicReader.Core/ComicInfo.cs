using System.Globalization;
using System.Xml.Linq;

namespace ComicReader.Core;

/// <summary>
/// Metadata parsed from a ComicInfo.xml file (ComicRack schema subset).
/// </summary>
public sealed class ComicInfo
{
    public string? Title { get; init; }
    public string? Series { get; init; }
    public string? Number { get; init; }
    public string? Count { get; init; }
    public string? Summary { get; init; }
    public string? Writer { get; init; }
    public string? Penciller { get; init; }
    public string? Genre { get; init; }
    public string? LanguageIso { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public int? Day { get; init; }
    public int? PageCount { get; init; }
    public string? Manga { get; init; }
    public bool? BlackAndWhite { get; init; }

    public bool IsRightToLeft =>
        string.Equals(Manga, "YesAndRightToLeft", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Manga, "RightToLeft", StringComparison.OrdinalIgnoreCase);

    public static ComicInfo? TryParse(string xml)
    {
        try
        {
            return Parse(xml);
        }
        catch
        {
            return null;
        }
    }

    public static ComicInfo Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidDataException("ComicInfo.xml has no root element.");
        return FromElement(root);
    }

    public static ComicInfo FromElement(XElement root)
    {
        string? Str(string name) => root.Element(name)?.Value?.Trim();
        int? Int(string name) =>
            int.TryParse(root.Element(name)?.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        bool? Bool(string name) =>
            bool.TryParse(root.Element(name)?.Value?.Trim(), out var v) ? v : null;

        return new ComicInfo
        {
            Title = NullIfEmpty(Str("Title")),
            Series = NullIfEmpty(Str("Series")),
            Number = NullIfEmpty(Str("Number")),
            Count = NullIfEmpty(Str("Count")),
            Summary = NullIfEmpty(Str("Summary")),
            Writer = NullIfEmpty(Str("Writer")),
            Penciller = NullIfEmpty(Str("Penciller")),
            Genre = NullIfEmpty(Str("Genre")),
            LanguageIso = NullIfEmpty(Str("LanguageISO")),
            Year = Int("Year"),
            Month = Int("Month"),
            Day = Int("Day"),
            PageCount = Int("PageCount"),
            Manga = NullIfEmpty(Str("Manga")),
            BlackAndWhite = Bool("BlackAndWhite"),
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
