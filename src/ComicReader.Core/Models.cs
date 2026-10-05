namespace ComicReader.Core;

public enum ReadingMode
{
    Single,
    Double,
    Continuous,
}

public enum ReadingDirection
{
    LeftToRight,
    RightToLeft,
}

public enum FitMode
{
    Width,
    Height,
    Original,
}

public sealed class BookEntry
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsFavorite { get; set; }
    public int LastPage { get; set; }
    public int TotalPages { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastReadUtc { get; set; }

    public double Progress => TotalPages > 0 ? Math.Clamp((LastPage + 1) / (double)TotalPages, 0, 1) : 0;

    public bool IsStarted => LastReadUtc is not null;
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public ReadingMode DefaultMode { get; set; } = ReadingMode.Single;
    public ReadingDirection Direction { get; set; } = ReadingDirection.LeftToRight;
    public FitMode DefaultFit { get; set; } = FitMode.Height;
    public int CacheMb { get; set; } = 512;
    public bool RememberPasswords { get; set; } = true;
    public bool CoverGlow { get; set; } = false;
    public List<string> LibraryFolders { get; set; } = new();
    public Dictionary<string, string> Passwords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastBook { get; set; }
}
