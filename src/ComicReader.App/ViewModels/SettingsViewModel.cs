using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComicReader.App.Services;
using ComicReader.Core;
using Microsoft.Win32;

namespace ComicReader.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _theme;
    [ObservableProperty] private ReadingMode _defaultMode;
    [ObservableProperty] private ReadingDirection _direction;
    [ObservableProperty] private FitMode _defaultFit;
    [ObservableProperty] private int _cacheMb;
    [ObservableProperty] private bool _rememberPasswords;

    public ObservableCollection<string> Folders { get; } = new();

    public IReadOnlyList<Option<string>> ThemeOptions { get; } = new[]
    {
        new Option<string>("Dark", "深色"),
        new Option<string>("Light", "浅色"),
    };

    public IReadOnlyList<Option<ReadingMode>> ModeOptions { get; } = new[]
    {
        new Option<ReadingMode>(ReadingMode.Single, "单页"),
        new Option<ReadingMode>(ReadingMode.Double, "双页"),
        new Option<ReadingMode>(ReadingMode.Continuous, "连续滚动"),
    };

    public IReadOnlyList<Option<ReadingDirection>> DirectionOptions { get; } = new[]
    {
        new Option<ReadingDirection>(ReadingDirection.LeftToRight, "从左到右"),
        new Option<ReadingDirection>(ReadingDirection.RightToLeft, "从右到左（日漫）"),
    };

    public IReadOnlyList<Option<FitMode>> FitOptions { get; } = new[]
    {
        new Option<FitMode>(FitMode.Height, "适应高度"),
        new Option<FitMode>(FitMode.Width, "适应宽度"),
        new Option<FitMode>(FitMode.Original, "原始尺寸"),
    };

    public SettingsViewModel()
    {
        var settings = AppServices.Settings;
        _theme = settings.Theme;
        _defaultMode = settings.DefaultMode;
        _direction = settings.Direction;
        _defaultFit = settings.DefaultFit;
        _cacheMb = settings.CacheMb;
        _rememberPasswords = settings.RememberPasswords;
        foreach (var folder in settings.LibraryFolders) Folders.Add(folder);
    }

    partial void OnThemeChanged(string value)
    {
        AppServices.Settings.Theme = value;
        AppServices.ApplyTheme(value);
        AppServices.SaveSettings();
    }

    partial void OnDefaultModeChanged(ReadingMode value)
    {
        AppServices.Settings.DefaultMode = value;
        AppServices.SaveSettings();
    }

    partial void OnDirectionChanged(ReadingDirection value)
    {
        AppServices.Settings.Direction = value;
        AppServices.SaveSettings();
    }

    partial void OnDefaultFitChanged(FitMode value)
    {
        AppServices.Settings.DefaultFit = value;
        AppServices.SaveSettings();
    }

    partial void OnCacheMbChanged(int value)
    {
        AppServices.Settings.CacheMb = value;
        AppServices.ImageCache.SetMaxBytes(AppServices.CacheBytes(value));
        AppServices.SaveSettings();
    }

    partial void OnRememberPasswordsChanged(bool value)
    {
        AppServices.Settings.RememberPasswords = value;
        AppServices.SaveSettings();
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "添加漫画文件夹", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var folder in dialog.FolderNames)
        {
            if (!Folders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
            {
                Folders.Add(folder);
            }

            AppServices.Library.AddFolder(folder);
        }

        AppServices.SaveSettings();
    }

    [RelayCommand]
    private void RemoveFolder(string? folder)
    {
        if (folder is null) return;
        Folders.Remove(folder);
        AppServices.Library.RemoveFolder(folder);
        AppServices.SaveSettings();
    }

    [RelayCommand]
    private void ClearPasswords()
    {
        var result = MessageBox.Show("清除所有已保存的压缩包密码？", "清除密码",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;
        AppServices.Settings.Passwords.Clear();
        AppServices.SaveSettings();
    }

    [RelayCommand]
    private void Back() => AppServices.Main.ShowLibrary();
}
