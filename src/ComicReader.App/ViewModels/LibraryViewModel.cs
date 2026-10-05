using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicReader.App.Services;
using ComicReader.Core;
using Microsoft.Win32;

namespace ComicReader.App.ViewModels;

public sealed partial class BookItemViewModel : ObservableObject
{
    public BookEntry Entry { get; }
    public string Path => Entry.Path;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private ImageSource? _cover;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _infoText = "";

    public BookItemViewModel(BookEntry entry)
    {
        Entry = entry;
        Sync();
    }

    public void Sync()
    {
        Title = string.IsNullOrWhiteSpace(Entry.Title) ? LibraryService.TitleForPath(Entry.Path) : Entry.Title;
        IsFavorite = Entry.IsFavorite;
        Progress = Entry.Progress * 100;
        ProgressText = Entry.TotalPages > 0
            ? $"{Math.Min(Entry.LastPage + 1, Entry.TotalPages)}/{Entry.TotalPages}"
            : "";
        InfoText = Entry.LastReadUtc is { } t ? $"上次阅读 {t.ToLocalTime():MM-dd HH:mm}" : "未阅读";
    }
}

public partial class LibraryViewModel : ObservableObject
{
    private readonly Dictionary<string, BookItemViewModel> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _coverRunning;
    private bool _coverDirty;

    public ObservableCollection<BookItemViewModel> Books { get; } = new();
    public ICollectionView BooksView { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _sortMode = "最近阅读";
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _recentOnly;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";

    public IReadOnlyList<string> SortModes { get; } = new[] { "最近阅读", "名称", "进度", "加入时间" };

    public LibraryViewModel()
    {
        BooksView = CollectionViewSource.GetDefaultView(Books);
        BooksView.Filter = o => o is BookItemViewModel item && PassesFilter(item);
        AppServices.Library.EntriesChanged += OnEntriesChanged;
        ReloadFromService();
    }

    private void OnEntriesChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) ReloadFromService();
        else dispatcher.BeginInvoke(ReloadFromService);
    }

    public void ReloadFromService()
    {
        var entries = AppServices.Library.Entries.ToList();

        foreach (var path in _itemsByPath.Keys.Where(p =>
                     !entries.Any(e => string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            _itemsByPath.Remove(path);
        }

        var list = new List<BookItemViewModel>();
        foreach (var entry in entries)
        {
            if (!_itemsByPath.TryGetValue(entry.Path, out var item))
            {
                item = new BookItemViewModel(entry);
                _itemsByPath[entry.Path] = item;
            }

            item.Sync();
            list.Add(item);
        }

        IEnumerable<BookItemViewModel> sorted = SortMode switch
        {
            "名称" => list.OrderBy(i => i.Title, NaturalStringComparer.Instance),
            "进度" => list.OrderByDescending(i => i.Entry.Progress).ThenBy(i => i.Title),
            "加入时间" => list.OrderByDescending(i => i.Entry.AddedUtc),
            _ => list.OrderByDescending(i => i.Entry.LastReadUtc ?? DateTime.MinValue).ThenBy(i => i.Title),
        };

        Books.Clear();
        foreach (var item in sorted) Books.Add(item);
        UpdateStatus();
        BooksView.Refresh();
        _ = RunCoverQueueAsync();
    }

    private void UpdateStatus()
    {
        var count = Books.Count;
        var filters = new List<string>();
        if (FavoritesOnly) filters.Add("收藏");
        if (RecentOnly) filters.Add("在读");
        StatusText = filters.Count > 0 ? $"共 {count} 本（{string.Join("、", filters)}）" : $"共 {count} 本";
    }

    private bool PassesFilter(BookItemViewModel item)
    {
        if (FavoritesOnly && !item.IsFavorite) return false;
        if (RecentOnly && !item.Entry.IsStarted) return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            if (item.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            return item.Path.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private async Task RunCoverQueueAsync()
    {
        _coverDirty = true;
        if (_coverRunning) return;
        _coverRunning = true;
        try
        {
            while (_coverDirty)
            {
                _coverDirty = false;
                var pending = Books.Where(b => b.Cover is null).ToList();
                foreach (var item in pending)
                {
                    var cover = await Task.Run(() => LoadOrGenerateCover(item.Path));
                    if (cover is not null) item.Cover = cover;
                }
            }
        }
        finally
        {
            _coverRunning = false;
        }
    }

    private static ImageSource? LoadOrGenerateCover(string path)
    {
        var existing = AppServices.Covers.Load(path);
        if (existing is not null) return existing;
        return AppServices.Covers.Generate(path) is not null ? AppServices.Covers.Load(path) : null;
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "添加漫画文件夹", Multiselect = true };
        if (dialog.ShowDialog() != true) return;

        var added = false;
        foreach (var folder in dialog.FolderNames)
        {
            added |= AppServices.Library.AddFolder(folder);
        }

        if (!added) return;
        AppServices.SaveSettings();
        _ = RescanInternalAsync();
    }

    [RelayCommand]
    private Task Rescan() => RescanInternalAsync();

    private async Task RescanInternalAsync()
    {
        IsBusy = true;
        BusyText = "正在扫描…";
        try
        {
            var progress = new Progress<string>(text => BusyText = text);
            await AppServices.Library.RescanAsync(progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    [RelayCommand]
    private void OpenSettings() => AppServices.Main.ShowSettings();

    [RelayCommand]
    private void Open(BookItemViewModel? item)
    {
        if (item is not null) AppServices.Main.OpenBook(item.Path);
    }

    [RelayCommand]
    private void ToggleFavorite(BookItemViewModel? item)
    {
        if (item is null) return;
        AppServices.Library.ToggleFavorite(item.Entry);
        item.Sync();
        BooksView.Refresh();
        UpdateStatus();
    }

    [RelayCommand]
    private void Remove(BookItemViewModel? item)
    {
        if (item is null) return;
        var result = MessageBox.Show($"从书库中移除「{item.Title}」？\n（不会删除磁盘上的文件）", "移出书库",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;
        AppServices.Library.Remove(item.Entry);
    }

    [RelayCommand]
    private void Reveal(BookItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer is unavailable; ignore.
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        BooksView.Refresh();
        UpdateStatus();
    }

    partial void OnFavoritesOnlyChanged(bool value)
    {
        BooksView.Refresh();
        UpdateStatus();
    }

    partial void OnRecentOnlyChanged(bool value)
    {
        BooksView.Refresh();
        UpdateStatus();
    }

    partial void OnSortModeChanged(string value) => ReloadFromService();
}
