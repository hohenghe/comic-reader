using System.Windows.Controls;
using System.Windows.Input;
using ComicReader.App.Services;
using ComicReader.App.ViewModels;

namespace ComicReader.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private void OnBookDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookList.SelectedItem is BookItemViewModel item)
        {
            AppServices.Main.OpenBook(item.Path);
        }
    }
}
