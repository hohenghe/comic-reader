using System.Windows;

namespace ComicReader.App.Views;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }
    public bool Remember { get; private set; }

    public PasswordDialog(string bookName, string? initial)
    {
        InitializeComponent();
        BookNameText.Text = bookName;
        if (!string.IsNullOrEmpty(initial)) Pwd.Password = initial;
        Loaded += (_, _) =>
        {
            Pwd.Focus();
            Pwd.SelectAll();
        };
    }

    public static (string? Password, bool Remember) Ask(Window? owner, string bookName, string? remembered)
    {
        var dialog = new PasswordDialog(bookName, remembered) { Owner = owner };
        var ok = dialog.ShowDialog() == true;
        return ok ? (dialog.Password, dialog.Remember) : (null, false);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Password = Pwd.Password;
        Remember = RememberCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
