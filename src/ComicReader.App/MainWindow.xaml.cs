using System.ComponentModel;
using System.IO;
using System.Windows;
using ComicReader.App.Services;

namespace ComicReader.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        AppServices.Main.CloseReader();
        AppServices.Library.Save();
        base.OnClosing(e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDropTarget(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var target = TryGetDropTarget(e);
        if (target is not null) AppServices.Main.OpenBook(target);
        e.Handled = true;
    }

    private static string? TryGetDropTarget(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return null;
        var first = files[0];
        return File.Exists(first) || Directory.Exists(first) ? first : null;
    }
}
