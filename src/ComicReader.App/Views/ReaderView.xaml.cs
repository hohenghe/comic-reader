using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComicReader.App.Services;
using ComicReader.App.ViewModels;
using ComicReader.Core;

namespace ComicReader.App.Views;

public partial class ReaderView : UserControl
{
    private ReaderViewModel? _vm;
    private Window? _window;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _thumbCts;
    private int _renderVersion;
    private int _thumbVersion;
    private bool _thumbsStarted;

    private double _userZoom = 1.0;
    private double _fitScale = 1.0;
    private double _contentW;
    private double _contentH;

    private bool _mouseDown;
    private bool _didDrag;
    private Point _mouseDownPos;
    private Point _panStartPos;
    private double _panStartX;
    private double _panStartY;

    private readonly List<Image> _scrollImages = new();
    private readonly List<Border> _scrollContainers = new();
    private readonly Dictionary<int, double> _aspects = new();
    private readonly HashSet<int> _scrollLoading = new();
    private bool _suppressPageSync;
    private bool _scrollOriginUpdate;
    private bool _syncingThumbSelection;
    private bool _scrollMouseDown;
    private Point _scrollMouseDownPos;

    public ReaderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.FullscreenChanged -= OnFullscreenChanged;
        }

        _vm = DataContext as ReaderViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.FullscreenChanged += OnFullscreenChanged;
        }

        if (IsLoaded && _vm is not null)
        {
            ResetViewState();
            RenderCurrent(resetPan: true);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null) _window.PreviewKeyDown += OnWindowKeyDown;
        Focus();

        if (_vm is not null && _scrollImages.Count == 0)
        {
            RenderCurrent(resetPan: true);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null) _window.PreviewKeyDown -= OnWindowKeyDown;
        _renderCts?.Cancel();
        _thumbCts?.Cancel();
        _thumbVersion++;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = _vm;
        if (vm is null) return;

        switch (e.PropertyName)
        {
            case nameof(ReaderViewModel.CurrentIndex):
                RenderCurrent(resetPan: true);
                SyncThumbSelection(vm.CurrentIndex);
                break;
            case nameof(ReaderViewModel.Mode):
                ResetViewState();
                RenderCurrent(resetPan: true);
                break;
            case nameof(ReaderViewModel.Direction):
            case nameof(ReaderViewModel.Fit):
                RenderCurrent(resetPan: false);
                break;
            case nameof(ReaderViewModel.ShowThumbnails):
                if (vm.ShowThumbnails) StartThumbs();
                break;
        }
    }

    private void ResetViewState()
    {
        Interlocked.Increment(ref _renderVersion);
        _renderCts?.Cancel();
        _userZoom = 1.0;
        PanTranslate.X = 0;
        PanTranslate.Y = 0;
        _scrollImages.Clear();
        _scrollContainers.Clear();
        _aspects.Clear();
        _scrollLoading.Clear();
        ScrollStack.Children.Clear();
        SingleImage.Source = null;
        LeftImage.Source = null;
        RightImage.Source = null;
    }

    private async void RenderCurrent(bool resetPan)
    {
        var vm = _vm;
        if (vm is null) return;

        var version = Interlocked.Increment(ref _renderVersion);
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;

        if (vm.Mode == ReadingMode.Continuous)
        {
            ShowContinuous(scrollToCurrent: !_scrollOriginUpdate);
            return;
        }

        PageRoot.Visibility = Visibility.Visible;
        ScrollHost.Visibility = Visibility.Collapsed;

        if (vm.Mode == ReadingMode.Single)
        {
            SingleImage.Visibility = Visibility.Visible;
            DoublePanel.Visibility = Visibility.Collapsed;
            await LoadIntoAsync(SingleImage, vm.CurrentIndex, cts.Token, version);
            if (version != _renderVersion) return;
        }
        else
        {
            SingleImage.Visibility = Visibility.Collapsed;
            DoublePanel.Visibility = Visibility.Visible;

            var start = vm.SpreadStart(vm.CurrentIndex);
            var leftIndex = vm.Direction == ReadingDirection.RightToLeft ? start + 1 : start;
            var rightIndex = vm.Direction == ReadingDirection.RightToLeft ? start : start + 1;

            await Task.WhenAll(
                LoadIntoAsync(LeftImage, leftIndex, cts.Token, version),
                LoadIntoAsync(RightImage, rightIndex, cts.Token, version));
            if (version != _renderVersion) return;
        }

        if (resetPan)
        {
            PanTranslate.X = 0;
            PanTranslate.Y = 0;
        }

        ApplyFit();
    }

    private async Task LoadIntoAsync(Image target, int index, CancellationToken ct, int version)
    {
        var vm = _vm;
        if (vm is null || index < 0 || index >= vm.PageCount)
        {
            target.Source = null;
            target.Width = 0;
            target.Height = 0;
            return;
        }

        var key = PageImageCache.MakeKey(vm.Book.Path, index);
        var cached = AppServices.ImageCache.TryGet(key);
        if (cached is not null)
        {
            SetImage(target, cached);
            return;
        }

        vm.IsLoading = true;
        try
        {
            var bitmap = await AppServices.ImageCache.GetAsync(key, () => vm.Book.ReadPageBytes(index), ct);
            if (ct.IsCancellationRequested || version != _renderVersion) return;
            SetImage(target, bitmap);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer render.
        }
        catch (Exception ex)
        {
            if (version == _renderVersion) vm.ErrorText = $"第 {index + 1} 页加载失败：{ex.Message}";
        }
        finally
        {
            if (version == _renderVersion) vm.IsLoading = false;
        }
    }

    private static void SetImage(Image target, BitmapSource source)
    {
        target.Source = source;
        target.Width = source.Width;
        target.Height = source.Height;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => ApplyFit();

    private void ApplyFit()
    {
        var vm = _vm;
        if (vm is null || vm.Mode == ReadingMode.Continuous) return;
        if (Viewport.ActualWidth < 10 || Viewport.ActualHeight < 10) return;

        if (vm.Mode == ReadingMode.Single)
        {
            if (SingleImage.Source is not { } single) return;
            _contentW = single.Width;
            _contentH = single.Height;
        }
        else
        {
            var lw = LeftImage.Source?.Width ?? 0;
            var rw = RightImage.Source?.Width ?? 0;
            if (lw + rw <= 0) return;
            _contentW = lw + rw + 8;
            _contentH = Math.Max(LeftImage.Source?.Height ?? 0, RightImage.Source?.Height ?? 0);
        }

        if (_contentW <= 0 || _contentH <= 0) return;

        _fitScale = vm.Fit switch
        {
            FitMode.Width => Viewport.ActualWidth * 0.98 / _contentW,
            FitMode.Height => Viewport.ActualHeight * 0.98 / _contentH,
            _ => 1.0,
        };

        if (double.IsNaN(_fitScale) || _fitScale <= 0) _fitScale = 1;

        ZoomScale.ScaleX = ZoomScale.ScaleY = _fitScale * _userZoom;
        ClampPan();
    }

    private void ClampPan()
    {
        var scaledW = _contentW * _fitScale * _userZoom;
        var scaledH = _contentH * _fitScale * _userZoom;
        var maxX = Math.Max(0, (scaledW - Viewport.ActualWidth) / 2);
        var maxY = Math.Max(0, (scaledH - Viewport.ActualHeight) / 2);
        PanTranslate.X = Math.Clamp(PanTranslate.X, -maxX, maxX);
        PanTranslate.Y = Math.Clamp(PanTranslate.Y, -maxY, maxY);
    }

    private void ZoomBy(double factor)
    {
        _userZoom = Math.Clamp(_userZoom * factor, 0.1, 12.0);
        ApplyFit();
    }

    private void OnViewportWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = _vm;
        if (vm is null) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
        }
        else if (e.Delta > 0)
        {
            vm.Prev();
        }
        else
        {
            vm.Next();
        }

        e.Handled = true;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _didDrag = false;
        _mouseDownPos = e.GetPosition(Viewport);
        _panStartPos = _mouseDownPos;
        _panStartX = PanTranslate.X;
        _panStartY = PanTranslate.Y;
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        var pos = e.GetPosition(Viewport);
        var dx = pos.X - _mouseDownPos.X;
        var dy = pos.Y - _mouseDownPos.Y;

        if (!_didDrag && Math.Abs(dx) + Math.Abs(dy) > 6) _didDrag = true;
        if (!_didDrag) return;

        PanTranslate.X = _panStartX + (pos.X - _panStartPos.X);
        PanTranslate.Y = _panStartY + (pos.Y - _panStartPos.Y);
        ClampPan();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        Viewport.ReleaseMouseCapture();
        if (_didDrag) return;

        var vm = _vm;
        if (vm is null) return;

        var pos = e.GetPosition(Viewport);
        var third = Viewport.ActualWidth / 3;
        if (pos.X < third)
        {
            if (vm.Direction == ReadingDirection.RightToLeft) vm.Next();
            else vm.Prev();
        }
        else if (pos.X > third * 2)
        {
            if (vm.Direction == ReadingDirection.RightToLeft) vm.Prev();
            else vm.Next();
        }
        else
        {
            vm.ToggleUi();
        }
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => _vm?.ToggleFullscreen();

    private void OnBackClick(object sender, RoutedEventArgs e) => AppServices.Main.CloseReader();

    private void OnFullscreenChanged(object? sender, EventArgs e)
    {
        var vm = _vm;
        var window = Window.GetWindow(this);
        if (vm is null || window is null) return;

        if (vm.IsFullscreen)
        {
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Maximized;
        }
        else
        {
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.ResizeMode = ResizeMode.CanResize;
            window.WindowState = WindowState.Normal;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var vm = _vm;
        if (vm is null) return;
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;

        switch (e.Key)
        {
            case Key.Left:
                if (vm.Direction == ReadingDirection.RightToLeft) vm.Next();
                else vm.Prev();
                break;
            case Key.Right:
                if (vm.Direction == ReadingDirection.RightToLeft) vm.Prev();
                else vm.Next();
                break;
            case Key.PageDown:
            case Key.Space:
            case Key.Down:
                vm.Next();
                break;
            case Key.PageUp:
            case Key.Up:
                vm.Prev();
                break;
            case Key.Home:
                vm.First();
                break;
            case Key.End:
                vm.Last();
                break;
            case Key.Add:
            case Key.OemPlus:
                ZoomBy(1.2);
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomBy(1 / 1.2);
                break;
            case Key.D0:
            case Key.NumPad0:
                _userZoom = 1.0;
                ApplyFit();
                break;
            case Key.F:
            case Key.F11:
                vm.ToggleFullscreen();
                break;
            case Key.T:
                vm.ToggleThumbnails();
                break;
            case Key.Escape:
                if (vm.IsFullscreen) vm.ToggleFullscreen();
                else AppServices.Main.CloseReader();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---------- continuous scroll mode ----------

    private void ShowContinuous(bool scrollToCurrent)
    {
        var vm = _vm;
        if (vm is null) return;

        PageRoot.Visibility = Visibility.Collapsed;
        ScrollHost.Visibility = Visibility.Visible;
        EnsureScrollItems();

        if (scrollToCurrent) ScrollToIndex(vm.CurrentIndex);
        LoadVisibleContinuous();
    }

    private void EnsureScrollItems()
    {
        var vm = _vm;
        if (vm is null) return;
        if (_scrollImages.Count == vm.PageCount) return;

        ScrollStack.Children.Clear();
        _scrollImages.Clear();
        _scrollContainers.Clear();
        _aspects.Clear();
        _scrollLoading.Clear();

        var width = Math.Max(120, (double.IsNaN(ScrollHost.ActualWidth) ? Viewport.ActualWidth : ScrollHost.ActualWidth) - 30);

        for (var i = 0; i < vm.PageCount; i++)
        {
            var aspect = _aspects.TryGetValue(i, out var a) ? a : 1.4;
            var image = new Image
            {
                Stretch = Stretch.Fill,
                Width = width,
                Height = Math.Max(1, width * aspect),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var container = new Border
            {
                Child = image,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ScrollStack.Children.Add(container);
            _scrollImages.Add(image);
            _scrollContainers.Add(container);
        }
    }

    private void ResizeScrollItems()
    {
        var width = Math.Max(120, ScrollHost.ActualWidth - 30);
        for (var i = 0; i < _scrollImages.Count; i++)
        {
            var aspect = _aspects.TryGetValue(i, out var a) ? a : 1.4;
            _scrollImages[i].Width = width;
            _scrollImages[i].Height = Math.Max(1, width * aspect);
        }
    }

    private void OnScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || vm.Mode != ReadingMode.Continuous) return;
        if (e.WidthChanged) ResizeScrollItems();
        LoadVisibleContinuous();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || vm.Mode != ReadingMode.Continuous) return;
        LoadVisibleContinuous();
    }

    private void LoadVisibleContinuous()
    {
        var vm = _vm;
        if (vm is null || _scrollContainers.Count == 0) return;

        var scrollTop = ScrollHost.VerticalOffset;
        var viewHeight = ScrollHost.ViewportHeight;

        var first = -1;
        var last = -1;
        double y = 0;

        for (var i = 0; i < _scrollContainers.Count; i++)
        {
            var h = _scrollContainers[i].ActualHeight + 6;
            var top = y;
            var bottom = y + h;
            y = bottom;

            if (bottom >= scrollTop && first < 0) first = i;
            if (top <= scrollTop + viewHeight) last = i;
        }

        if (first < 0) first = 0;
        if (last < 0) last = Math.Min(_scrollContainers.Count - 1, first);

        for (var i = Math.Max(0, first - 2); i <= Math.Min(_scrollContainers.Count - 1, last + 2); i++)
        {
            StartScrollLoad(i);
        }

        for (var i = 0; i < _scrollContainers.Count; i++)
        {
            if (i < first - 6 || i > last + 6) _scrollImages[i].Source = null;
        }

        if (!_suppressPageSync && first >= 0 && vm.CurrentIndex != first)
        {
            _scrollOriginUpdate = true;
            vm.GoTo(first);
            _scrollOriginUpdate = false;
        }
    }

    private async void StartScrollLoad(int index)
    {
        var vm = _vm;
        if (vm is null || index < 0 || index >= _scrollImages.Count) return;

        var image = _scrollImages[index];
        if (image.Source is not null || !_scrollLoading.Add(index)) return;

        var version = _renderVersion;
        try
        {
            var key = PageImageCache.MakeKey(vm.Book.Path, index);
            var bitmap = AppServices.ImageCache.TryGet(key)
                         ?? await AppServices.ImageCache.GetAsync(key, () => vm.Book.ReadPageBytes(index), CancellationToken.None);

            if (version != _renderVersion) return;

            image.Source = bitmap;
            var aspect = bitmap.Width > 0 ? bitmap.Height / (double)bitmap.Width : 1.4;
            _aspects[index] = aspect;
            image.Height = Math.Max(1, image.Width * aspect);
        }
        catch (Exception ex)
        {
            if (version == _renderVersion) vm.ErrorText = $"第 {index + 1} 页加载失败：{ex.Message}";
        }
        finally
        {
            _scrollLoading.Remove(index);
        }
    }

    private void ScrollToIndex(int index)
    {
        if (index < 0 || index >= _scrollContainers.Count) return;

        _suppressPageSync = true;
        _scrollContainers[index].BringIntoView();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _suppressPageSync = false;
            LoadVisibleContinuous();
        }), DispatcherPriority.ContextIdle);
    }

    private void OnScrollMouseDown(object sender, MouseButtonEventArgs e)
    {
        _scrollMouseDown = true;
        _scrollMouseDownPos = e.GetPosition(ScrollHost);
    }

    private void OnScrollMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_scrollMouseDown) return;
        _scrollMouseDown = false;
        var pos = e.GetPosition(ScrollHost);
        if (Math.Abs(pos.X - _scrollMouseDownPos.X) + Math.Abs(pos.Y - _scrollMouseDownPos.Y) > 6) return;
        _vm?.ToggleUi();
    }

    // ---------- thumbnails ----------

    private void StartThumbs()
    {
        var vm = _vm;
        if (vm is null || _thumbsStarted) return;
        _thumbsStarted = true;
        _ = LoadThumbsAsync(vm);
    }

    private async Task LoadThumbsAsync(ReaderViewModel vm)
    {
        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;
        var version = ++_thumbVersion;

        for (var i = 0; i < vm.PageCount; i++)
        {
            if (ct.IsCancellationRequested || version != _thumbVersion) return;

            var item = vm.Thumbs[i];
            if (item.Image is not null) continue;

            try
            {
                var bitmap = await Task.Run(() =>
                {
                    var bytes = vm.Book.ReadPageBytes(i);
                    return ImageDecoder.Decode(bytes, decodePixelHeight: 160);
                }, ct);

                if (bitmap is not null && version == _thumbVersion) item.Image = bitmap;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Skip pages that fail to decode.
            }
        }
    }

    private void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingThumbSelection || _vm is null) return;
        if (ThumbList.SelectedItem is ThumbItemViewModel item) _vm.GoTo(item.Index);
    }

    private void SyncThumbSelection(int index)
    {
        var vm = _vm;
        if (vm is null || !vm.ShowThumbnails) return;
        if (index < 0 || index >= ThumbList.Items.Count) return;
        if (ThumbList.SelectedIndex == index) return;

        _syncingThumbSelection = true;
        ThumbList.SelectedIndex = index;
        ThumbList.ScrollIntoView(ThumbList.Items[index]);
        _syncingThumbSelection = false;
    }
}
