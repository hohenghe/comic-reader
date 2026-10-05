using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicReader.App.Services;
using ComicReader.App.Views;
using ComicReader.Core;

namespace ComicReader.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private ReaderViewModel? _reader;

    [ObservableProperty]
    private object? _currentView;

    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel()
    {
        Library = new LibraryViewModel();
        Settings = new SettingsViewModel();
        CurrentView = Library;
    }

    public void ShowLibrary()
    {
        CloseReader();
        CurrentView = Library;
        Library.ReloadFromService();
    }

    public void ShowSettings() => CurrentView = Settings;

    public void OpenBook(BookEntry entry) => OpenBook(entry.Path);

    public void OpenBook(string path)
    {
        ComicBook? book = null;
        try
        {
            try
            {
                book = ComicBook.Open(path);
            }
            catch (ComicBookException ex) when (ex.NeedsPassword)
            {
                var remembered = AppServices.Settings.Passwords.TryGetValue(path, out var saved) ? saved : null;
                var result = PasswordDialog.Ask(Application.Current?.MainWindow, System.IO.Path.GetFileName(path), remembered);
                if (result.Password is null) return;

                try
                {
                    book = ComicBook.Open(path, result.Password);
                }
                catch (ComicBookException)
                {
                    MessageBox.Show("密码不正确，或该压缩包无法打开。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (result.Remember && AppServices.Settings.RememberPasswords)
                {
                    AppServices.Settings.Passwords[path] = result.Password;
                    AppServices.SaveSettings();
                }
            }
        }
        catch (ComicBookException ex)
        {
            MessageBox.Show(ex.Message, "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (book is null) return;

        var entry = AppServices.Library.EnsureEntry(path, book.Title, book.Pages.Count);
        AppServices.Library.Save();

        CloseReader();
        _reader = new ReaderViewModel(book, entry);
        CurrentView = _reader;
    }

    public void CloseReader()
    {
        var reader = _reader;
        _reader = null;
        if (reader is null) return;
        reader.Dispose();
        AppServices.Library.Save();
        Library.ReloadFromService();
    }
}
