using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicReader.App.Services;
using ComicReader.Core;

namespace ComicReader.App.ViewModels;

public sealed partial class ThumbItemViewModel : ObservableObject
{
    public int Index { get; }
    public string Label { get; }

    [ObservableProperty] private ImageSource? _image;

    public ThumbItemViewModel(int index)
    {
        Index = index;
        Label = (index + 1).ToString();
    }
}

public partial class ReaderViewModel : ObservableObject, IDisposable
{
    private readonly BookEntry? _entry;
    private readonly DispatcherTimer _saveTimer;
    private bool _pendingSave;
    private bool _disposed;

    public ComicBook Book { get; }
    public int PageCount => Book.Pages.Count;
    public string Title => Book.Title;
    public string PageLabel => PageCount > 0 ? $"{Math.Clamp(CurrentIndex, 0, PageCount - 1) + 1} / {PageCount}" : "0 / 0";
    public int MaxIndex => Math.Max(0, PageCount - 1);

    public ObservableCollection<ThumbItemViewModel> Thumbs { get; } = new();

    public IReadOnlyList<Option<ReadingMode>> ModeOptions { get; } = new[]
    {
        new Option<ReadingMode>(ReadingMode.Single, "单页"),
        new Option<ReadingMode>(ReadingMode.Double, "双页"),
        new Option<ReadingMode>(ReadingMode.Continuous, "连续滚动"),
    };

    public IReadOnlyList<Option<ReadingDirection>> DirectionOptions { get; } = new[]
    {
        new Option<ReadingDirection>(ReadingDirection.LeftToRight, "从左到右"),
        new Option<ReadingDirection>(ReadingDirection.RightToLeft, "从右到左"),
    };

    public IReadOnlyList<Option<FitMode>> FitOptions { get; } = new[]
    {
        new Option<FitMode>(FitMode.Height, "适应高度"),
        new Option<FitMode>(FitMode.Width, "适应宽度"),
        new Option<FitMode>(FitMode.Original, "原始尺寸"),
    };

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private ReadingMode _mode;
    [ObservableProperty] private ReadingDirection _direction;
    [ObservableProperty] private FitMode _fit;
    [ObservableProperty] private bool _isUiVisible = true;
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private bool _showThumbnails;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorText = "";

    public event EventHandler? RenderRequested;
    public event EventHandler? FullscreenChanged;

    public ReaderViewModel(ComicBook book, BookEntry? entry)
    {
        Book = book;
        _entry = entry;

        _mode = AppServices.Settings.DefaultMode;
        _fit = AppServices.Settings.DefaultFit;
        _direction = book.Info?.IsRightToLeft == true
            ? ReadingDirection.RightToLeft
            : AppServices.Settings.Direction;

        var start = Math.Clamp(entry?.LastPage ?? 0, 0, Math.Max(0, PageCount - 1));
        _currentIndex = _mode == ReadingMode.Double ? SpreadStart(start) : start;

        for (var i = 0; i < PageCount; i++) Thumbs.Add(new ThumbItemViewModel(i));

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            FlushProgress();
        };
    }

    public int SpreadStart(int index) => index <= 0 ? 0 : 1 + (index - 1) / 2 * 2;

    public void Next()
    {
        if (Mode == ReadingMode.Double)
        {
            var start = SpreadStart(CurrentIndex);
            var next = start == 0 ? 1 : start + 2;
            if (next < PageCount) CurrentIndex = next;
        }
        else if (CurrentIndex < PageCount - 1)
        {
            CurrentIndex++;
        }
    }

    public void Prev()
    {
        if (Mode == ReadingMode.Double)
        {
            var start = SpreadStart(CurrentIndex);
            CurrentIndex = start > 1 ? start - 2 : 0;
        }
        else if (CurrentIndex > 0)
        {
            CurrentIndex--;
        }
    }

    public void GoTo(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, PageCount - 1));
        CurrentIndex = Mode == ReadingMode.Double ? SpreadStart(index) : index;
    }

    public void First() => GoTo(0);

    public void Last() => GoTo(PageCount - 1);

    public void ToggleUi() => IsUiVisible = !IsUiVisible;

    public void ToggleThumbnails() => ShowThumbnails = !ShowThumbnails;

    public void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
        FullscreenChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PageLabel));
        RequestSave();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnModeChanged(ReadingMode value)
    {
        if (value == ReadingMode.Double) CurrentIndex = SpreadStart(CurrentIndex);
        AppServices.Settings.DefaultMode = value;
        AppServices.SaveSettings();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDirectionChanged(ReadingDirection value)
    {
        AppServices.Settings.Direction = value;
        AppServices.SaveSettings();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnFitChanged(FitMode value)
    {
        AppServices.Settings.DefaultFit = value;
        AppServices.SaveSettings();
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestSave()
    {
        if (_entry is null) return;
        _pendingSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushProgress()
    {
        if (!_pendingSave || _entry is null) return;
        _pendingSave = false;
        AppServices.Library.SaveProgress(Book.Path, CurrentIndex, PageCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _saveTimer.Stop();
        _pendingSave = true;
        FlushProgress();
        Book.Dispose();
    }
}
