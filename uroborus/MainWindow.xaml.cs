using Microsoft.Win32;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace uroborus
{
    public class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            CheckReentrancy();
            var itemsList = collection.ToList();
            if (itemsList.Count == 0) return;

            if (Items is List<T> list)
                list.InsertRange(index, itemsList);
            else
                foreach (var item in itemsList) Items.Insert(index++, item);

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public class TrackViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _path = "";
        public string Path { get => _path; set { if (_path != value) { _path = value; OnPropertyChanged(nameof(Path)); } } }

        private string _title = "";
        public string Title { get => _title; set { if (_title != value) { _title = value; OnPropertyChanged(nameof(Title)); } } }

        private string _duration = "";
        public string Duration { get => _duration; set { if (_duration != value) { _duration = value; OnPropertyChanged(nameof(Duration)); } } }

        private string _info = "";
        public string Info { get => _info; set { if (_info != value) { _info = value; OnPropertyChanged(nameof(Info)); } } }

        private bool _isCurrent;
        public bool IsCurrent { get => _isCurrent; set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(nameof(IsCurrent)); } } }

        private bool _isLiked;
        public bool IsLiked { get => _isLiked; set { if (_isLiked != value) { _isLiked = value; OnPropertyChanged(nameof(IsLiked)); } } }

        private int _displayIndex;
        public int DisplayIndex { get => _displayIndex; set { if (_displayIndex != value) { _displayIndex = value; OnPropertyChanged(nameof(DisplayIndex)); } } }

        private bool _isDragging;
        public bool IsDragging { get => _isDragging; set { if (_isDragging != value) { _isDragging = value; OnPropertyChanged(nameof(IsDragging)); } } }
    }

    public class Playlist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public List<string> Tracks { get; set; } = [];
        public bool IsLiked { get; set; } = false;
    }

    public class SessionData
    {
        public List<string> QueuePaths { get; set; } = [];
        public int QueueIndex { get; set; } = -1;
        public double Volume { get; set; } = 0.5;
        public double Position { get; set; } = 0;
        public bool IsLoop { get; set; } = false;
        public bool IsShuffle { get; set; } = false;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
    }

    internal class DarkMenuColorTable : System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(15, 15, 15);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.FromArgb(15, 15, 15);
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.FromArgb(15, 15, 15);
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.FromArgb(15, 15, 15);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(50, 50, 50);
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(50, 50, 50);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.Transparent;
    }

    internal class DarkMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable()) { }
        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected) { using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(40, 40, 40)); e.Graphics.FillRectangle(brush, e.Item.ContentRectangle); }
            else base.OnRenderMenuItemBackground(e);
        }
    }

    public partial class MainWindow : Window
    {
        [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int StrCmpLogicalW(string psz1, string psz2);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_M = 0x4D;

        private static Mutex? _singleInstanceMutex;
        private const string PipeName = "UroborusPlayerPipe";

        private IntPtr _windowHandle;
        private HwndSource? _source;

        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioFile;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly DispatcherTimer _dragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Random _random = new();

        private readonly ObservableRangeCollection<TrackViewModel> _queueBindable = [];
        private readonly ObservableRangeCollection<TrackViewModel> _detailBindable = [];

        private int _qIndex = -1;
        private bool _isSeekDragging = false;

        private Border? _currentDropLine = null;
        private TrackViewModel? _draggedQueueItem = null;
        private TrackViewModel? _draggedDetailItem = null;
        private ListBox? _activeDragListBox = null;

        private bool _isDragInProgress = false;

        private Point? _dragStartPos = null;
        private TrackViewModel? _dragItem = null;
        private bool _isLoop = false;
        private bool _isShuffle = false;
        private double _volumeBeforeMute = -1;
        private bool _isMiniMode = false;
        private double _normalLeft = double.NaN;
        private double _normalTop = double.NaN;

        private List<Playlist> _playlists = [];
        private Playlist? _detailPl = null;
        private string _currentPlaylistId = "";
        private bool _drawerOpen = false;
        private bool _searchOpen = false;
        private bool _hotkeysOpen = false;
        private string _searchQuery = "";

        private bool _detailSearchOpen = false;
        private string _detailSearchQuery = "";
        private readonly System.Windows.Forms.NotifyIcon _trayIcon = new();

        private DateTime _lastDropTime = DateTime.MinValue;

        private static readonly string AppDataFolder = EnsureAppDataFolder();
        private static readonly string SessionPath = System.IO.Path.Combine(AppDataFolder, "uroborus_session.json");
        private static readonly string PlaylistsPath = System.IO.Path.Combine(AppDataFolder, "uroborus_playlists.json");

        private static string EnsureAppDataFolder()
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = System.IO.Path.Combine(roaming, "Uroborus");
            Directory.CreateDirectory(folder); // Создаёт если нет, ничего не делает если есть
            return folder;
        }
        private static readonly HashSet<string> SupportedExts = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" };

        private static readonly ConcurrentDictionary<string, (string dur, string info, string title)> _metaCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private static readonly object _ioLock = new();

        public MainWindow()
        {
            _singleInstanceMutex = new Mutex(true, "UroborusSingleInstanceAppMutex", out bool createdNew);
            if (!createdNew)
            {
                SendArgsToRunningInstance();
                Environment.Exit(0);
                return;
            }

            InitializeComponent();
            QueueList.ItemsSource = _queueBindable;
            DetailTrackList.ItemsSource = _detailBindable;

            _timer.Tick += Timer_Tick;
            _dragScrollTimer.Tick += DragScrollTimer_Tick;

            SetupTrayIcon();
            RegisterContextMenu();
            LoadPlaylists();
            RestoreSession();
            if (_qIndex < 0) UpdateTrackTitle("No Track", "");
            StartPipeServer();

            this.Loaded += MainWindow_Loaded;
        }
        private void DetailTrack_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.DataContext is TrackViewModel vm && _detailPl != null)
            {
                var cm = new ContextMenu { Style = (Style)FindResource("ModernMenu") };
                MenuItem MakeItem(string header, Action onClick, bool isDanger = false)
                {
                    var mi = new MenuItem { Header = header, Style = (Style)FindResource("ModernMenuItem") };
                    if (isDanger) mi.Foreground = (SolidColorBrush)FindResource("Accent");
                    mi.Click += (_, __) => onClick(); return mi;
                }

                cm.Items.Add(MakeItem(IsLiked(vm.Path) ? "♥  Убрать из Любимых" : "♡  Добавить в Любимые", () => ToggleLike(vm.Path)));
                cm.Items.Add(new Separator { Style = (Style)FindResource("ModernSeparator") });
                cm.Items.Add(MakeItem("↕  Переместить на позицию...", () =>
                {
                    var existing = _detailPl.Tracks.Where(File.Exists).ToList();

                    // Берём индекс напрямую из DisplayIndex который был задан при построении списка
                    int currentPos = vm.DisplayIndex;
                    if (currentPos <= 0 || currentPos > existing.Count) return;

                    var dlg = new MoveToPositionDialog(currentPos, existing.Count) { Owner = this };
                    if (dlg.ShowDialog() != true) return;

                    int from = currentPos - 1;
                    int to = dlg.TargetPosition - 1;
                    if (from == to) return;

                    string pathToMove = existing[from];
                    existing.RemoveAt(from);
                    existing.Insert(to, pathToMove);

                    _detailPl.Tracks = existing;

                    SavePlaylists();
                    RefreshDetailView();
                }));
                cm.Items.Add(new Separator { Style = (Style)FindResource("ModernSeparator") });
                cm.Items.Add(MakeItem("Удалить из плейлиста", () =>
                {
                    _detailPl.Tracks.Remove(vm.Path);
                    SavePlaylists();
                    RefreshDetailView();
                }, isDanger: true));

                cm.PlacementTarget = b;
                cm.IsOpen = true;
                e.Handled = true;
            }
        }
        private static void RegisterContextMenu()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                foreach (string ext in SupportedExts)
                {
                    string keyPath = $@"Software\Classes\SystemFileAssociations\{ext}\shell\Uroborus";
                    using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
                    if (key != null)
                    {
                        key.SetValue("", "Слушать в Uroborus");
                        key.SetValue("Icon", $"\"{exePath}\"");
                        using var cmdKey = key.CreateSubKey("command");
                        cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                string folderKeyPath = @"Software\Classes\Directory\shell\Uroborus";
                using var folderKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(folderKeyPath);
                if (folderKey != null)
                {
                    folderKey.SetValue("", "Слушать в Uroborus");
                    folderKey.SetValue("Icon", $"\"{exePath}\"");
                    using var cmdKey = folderKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                }
            }
            catch (Exception ex) { Debug.WriteLine($"RegisterContextMenu Error: {ex}"); }
        }

        private static void UnregisterContextMenu()
        {
            try
            {
                foreach (string ext in SupportedExts)
                {
                    string keyPath = $@"Software\Classes\SystemFileAssociations\{ext}\shell\Uroborus";
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
                }

                string folderKeyPath = @"Software\Classes\Directory\shell\Uroborus";
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(folderKeyPath, false);

                MessageBox.Show("Uroborus успешно удален из меню Windows.\nДо следующего запуска плеера кнопки не будет.", "Uroborus", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при очистке реестра: " + ex.Message, "Uroborus", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void SendArgsToRunningInstance()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(1000);
                    using var writer = new StreamWriter(client);
                    writer.WriteLine(args[1]);
                    writer.Flush();
                }
                catch (Exception ex) { Debug.WriteLine($"SendArgs Error: {ex}"); }
            }
        }

        private async void StartPipeServer()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    string? path = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(path))
                    {
                        _ = Dispatcher.InvokeAsync(async () =>
                        {
                            RestoreWindow();
                            await HandleExternalFileAsync(path);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Pipe Server Error: {ex}");
                    await Task.Delay(1000);
                }
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                await HandleExternalFileAsync(args[1]);
            }
        }

        private async Task HandleExternalFileAsync(string targetPath)
        {
            if (Directory.Exists(targetPath))
            {
                int oldQueueCount = _queueBindable.Count;
                await AddPathsAsync([targetPath]);

                if (_queueBindable.Count > oldQueueCount)
                {
                    _qIndex = 0;
                    Load(_queueBindable[_qIndex].Path, true);
                    QueueList.ScrollIntoView(_queueBindable[_qIndex]);
                }
                return;
            }

            if (!File.Exists(targetPath)) return;
            if (!SupportedExts.Contains(Path.GetExtension(targetPath))) return;

            int existingIdx = -1;
            for (int i = 0; i < _queueBindable.Count; i++)
            {
                if (string.Equals(_queueBindable[i].Path, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    existingIdx = i;
                    break;
                }
            }

            if (existingIdx >= 0)
            {
                _qIndex = existingIdx;
                Load(_queueBindable[_qIndex].Path, true);
                QueueList.ScrollIntoView(_queueBindable[_qIndex]);
            }
            else
            {
                await AddPathsAsync([targetPath]);
                int newIdx = -1;
                for (int i = 0; i < _queueBindable.Count; i++)
                {
                    if (string.Equals(_queueBindable[i].Path, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        newIdx = i;
                        break;
                    }
                }

                if (newIdx >= 0)
                {
                    _qIndex = newIdx;
                    Load(_queueBindable[_qIndex].Path, true);
                    QueueList.ScrollIntoView(_queueBindable[_qIndex]);
                }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);
            RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL, VK_M);
        }

        private void ShowLoading(string text)
        {
            LoadingText.Text = text;
            LoadingPill.Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            var slide = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            LoadingPill.BeginAnimation(UIElement.OpacityProperty, fade);
            LoadingPillTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
            LoadingSpinnerTransform.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        private async void HideLoading()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            var slide = new DoubleAnimation(0, 20, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };

            LoadingPill.BeginAnimation(UIElement.OpacityProperty, fade);
            LoadingPillTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            await Task.Delay(200);
            LoadingSpinnerTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingPill.Visibility = Visibility.Collapsed;
        }

        private void DragScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (_activeDragListBox == null) { _dragScrollTimer.Stop(); return; }

            ScrollViewer? sv = GetScrollViewer(_activeDragListBox);
            if (sv == null) return;

            GetCursorPos(out POINT screenPt);
            Point listBoxPt = _activeDragListBox.PointFromScreen(new Point(screenPt.X, screenPt.Y));

            double height = _activeDragListBox.ActualHeight;
            double zone = 50;
            double maxSpeed = 12;

            if (listBoxPt.Y < zone)
            {
                double speed = maxSpeed * (1.0 - Math.Max(0, listBoxPt.Y) / zone);
                sv.ScrollToVerticalOffset(sv.VerticalOffset - speed);
            }
            else if (listBoxPt.Y > height - zone)
            {
                double speed = maxSpeed * (1.0 - Math.Max(0, height - listBoxPt.Y) / zone);
                sv.ScrollToVerticalOffset(sv.VerticalOffset + speed);
            }
        }

        private void StartDragScroll(ListBox listBox)
        {
            _activeDragListBox = listBox;
            _dragScrollTimer.Start();
        }

        private void StopDragScroll()
        {
            _dragScrollTimer.Stop();
            _activeDragListBox = null;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (this.Visibility == Visibility.Visible && this.WindowState == WindowState.Normal && _isMiniMode)
                    this.WindowState = WindowState.Minimized;
                else
                {
                    SetMiniMode(true);
                    if (this.WindowState == WindowState.Minimized || this.Visibility != Visibility.Visible)
                    {
                        this.Show();
                        this.WindowState = WindowState.Normal;
                    }
                    this.Activate();
                    this.Topmost = true;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void SetMiniMode(bool enable)
        {
            if (_isMiniMode == enable) return;
            _isMiniMode = enable;

            if (_isMiniMode)
            {
                _normalLeft = this.Left;
                _normalTop = this.Top;
                NormalModeGrid.Visibility = Visibility.Collapsed;
                MiniModeGrid.Visibility = Visibility.Visible;
                this.Width = 350; this.Height = 110;
                this.Topmost = true;
                RootBorder.CornerRadius = new CornerRadius(18);
                var area = SystemParameters.WorkArea;
                this.Left = area.Right - this.Width - 24;
                this.Top = area.Bottom - this.Height - 24;
            }
            else
            {
                MiniModeGrid.Visibility = Visibility.Collapsed;
                NormalModeGrid.Visibility = Visibility.Visible;
                this.Width = 360; this.Height = 660;
                this.Topmost = false;
                RootBorder.CornerRadius = new CornerRadius(26);
                if (!double.IsNaN(_normalLeft)) { this.Left = _normalLeft; this.Top = _normalTop; }
            }

            PlayPauseBtn.ApplyTemplate();
            MiniPlayPauseBtn.ApplyTemplate();
            UpdatePlayIcon(_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing);
        }

        private void MiniPlayerBtn_Click(object sender, RoutedEventArgs e) => SetMiniMode(!_isMiniMode);

        private void SetupTrayIcon()
        {
            _trayIcon.Text = "Uroborus";
            _trayIcon.Visible = false;
            try
            {
                var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/logo.ico"))?.Stream;
                if (iconStream != null) _trayIcon.Icon = new System.Drawing.Icon(iconStream);
                else _trayIcon.Icon = System.Drawing.SystemIcons.Asterisk;
            }
            catch { _trayIcon.Icon = System.Drawing.SystemIcons.Asterisk; }

            _trayIcon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) RestoreWindow(); };

            var trayMenu = new System.Windows.Forms.ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowImageMargin = false,
                BackColor = System.Drawing.Color.FromArgb(15, 15, 15),
                Padding = new System.Windows.Forms.Padding(2, 4, 2, 4)
            };

            System.Windows.Forms.ToolStripItem AddItem(string text, EventHandler onClick)
            {
                var item = trayMenu.Items.Add(text, null, onClick);
                item.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
                item.Font = new System.Drawing.Font("Segoe UI", 9F);
                item.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
                return item;
            }

            AddItem("Развернуть", (s, e) => RestoreWindow());
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            AddItem("▶/⏸  Играть / Пауза", (s, e) => PlayPauseBtn_Click(null!, null!));
            AddItem("⏭  Следующий трек", (s, e) => NextBtn_Click(null!, null!));
            AddItem("⏮  Предыдущий трек", (s, e) => PrevBtn_Click(null!, null!));
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            AddItem("🧹  Отвязать от Windows", (s, e) => UnregisterContextMenu());
            AddItem("❌  Закрыть", (s, e) => Close());

            _trayIcon.ContextMenuStrip = trayMenu;
        }

        private void RestoreWindow()
        {
            if (this.WindowState == WindowState.Minimized || this.Visibility != Visibility.Visible)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
            _trayIcon.Visible = false;
        }

        private static (string dur, string info, string title) GetCachedMeta(string path)
        {
            if (_metaCache.TryGetValue(path, out var m)) return m;
            if (_metaCache.Count > 2000) _metaCache.Clear();
            var meta = GetTrackMetadataFast(path);
            _metaCache[path] = meta;
            return meta;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized) { this.Hide(); _trayIcon.Visible = true; }
            base.OnStateChanged(e);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_audioFile == null) return;
            if (!_isSeekDragging)
            {
                ProgressSlider.Value = _audioFile.CurrentTime.TotalSeconds;
                MiniProgressSlider.Value = _audioFile.CurrentTime.TotalSeconds;
                CurrentTimeText.Text = FormatTime(_audioFile.CurrentTime);
                MiniCurrentTimeText.Text = FormatTime(_audioFile.CurrentTime);
            }
        }

        private void UpdateCover(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) { SetDefaultCover(); return; }
            try
            {
                using var file = TagLib.File.Create(filePath);
                if (file.Tag.Pictures.Length > 0)
                {
                    var pic = file.Tag.Pictures[0];
                    using var ms = new MemoryStream(pic.Data.Data);
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    CoverImageBrush.ImageSource = bmp; ImageCoverLayer.Visibility = Visibility.Visible; DefaultCoverLayer.Visibility = Visibility.Collapsed;
                    MiniCoverImageBrush.ImageSource = bmp; MiniImageCoverLayer.Visibility = Visibility.Visible; MiniDefaultCoverLayer.Visibility = Visibility.Collapsed;
                    return;
                }
            }
            catch { }
            SetDefaultCover();
        }

        private void SetDefaultCover()
        {
            ImageCoverLayer.Visibility = Visibility.Collapsed; DefaultCoverLayer.Visibility = Visibility.Visible;
            MiniImageCoverLayer.Visibility = Visibility.Collapsed; MiniDefaultCoverLayer.Visibility = Visibility.Visible;
        }

        private void MarqueeContainer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrackTitle(TrackNameText.Text, TrackSubText.Text);

        private void UpdateTrackTitle(string title, string subtitle)
        {
            TrackNameText.Text = string.IsNullOrEmpty(title) ? "No Track" : title;
            TrackSubText.Text = subtitle;
            MiniTrackNameText.Text = string.IsNullOrEmpty(title) ? "No Track" : title;
            MiniTrackSubText.Text = subtitle;
            string trayText = string.IsNullOrEmpty(subtitle) ? title : $"{subtitle} - {title}";
            if (trayText.Length > 63) trayText = string.Concat(trayText.AsSpan(0, 60), "...");
            if (_trayIcon != null) _trayIcon.Text = string.IsNullOrEmpty(trayText) ? "Uroborus" : trayText;

            MarqueePanel.BeginAnimation(Canvas.LeftProperty, null);
            TrackNameTextClone.Visibility = Visibility.Collapsed;
            TrackNameText.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double textWidth = TrackNameText.DesiredSize.Width;
            double containerWidth = MarqueePanel.Parent is FrameworkElement p ? p.ActualWidth : 220;
            if (containerWidth == 0) containerWidth = 220;

            if (textWidth > containerWidth && !string.IsNullOrEmpty(subtitle))
            {
                TrackNameTextClone.Text = TrackNameText.Text;
                TrackNameTextClone.Visibility = Visibility.Visible;
                double totalWidth = textWidth + 40;
                var anim = new DoubleAnimation(0, -totalWidth, TimeSpan.FromSeconds(totalWidth / 35.0)) { RepeatBehavior = RepeatBehavior.Forever };
                MarqueePanel.BeginAnimation(Canvas.LeftProperty, anim);
            }
            else
            {
                Canvas.SetLeft(MarqueePanel, (containerWidth - textWidth) / 2);
            }

            MiniMarqueePanel.BeginAnimation(Canvas.LeftProperty, null);
            MiniTrackNameTextClone.Visibility = Visibility.Collapsed;
            MiniTrackNameText.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double miniTextWidth = MiniTrackNameText.DesiredSize.Width;
            double miniContainerWidth = MiniMarqueePanel.Parent is FrameworkElement mp ? mp.ActualWidth : 150;
            if (miniContainerWidth == 0) miniContainerWidth = 150;
            if (miniTextWidth > miniContainerWidth && !string.IsNullOrEmpty(subtitle))
            {
                MiniTrackNameTextClone.Text = MiniTrackNameText.Text;
                MiniTrackNameTextClone.Visibility = Visibility.Visible;
                double totalWidth = miniTextWidth + 40;
                var anim = new DoubleAnimation(0, -totalWidth, TimeSpan.FromSeconds(totalWidth / 35.0)) { RepeatBehavior = RepeatBehavior.Forever };
                MiniMarqueePanel.BeginAnimation(Canvas.LeftProperty, anim);
            }
            else
            {
                Canvas.SetLeft(MiniMarqueePanel, 0);
            }
        }

        private void Load(string path, bool autoPlay = true, double seekTo = 0)
        {
            try
            {
                StopAndDispose();
                UpdateCover(path);
                var (dur, info, title) = GetCachedMeta(path);
                UpdateTrackTitle(title, Path.GetFileName(Path.GetDirectoryName(path) ?? ""));

                _audioFile = new AudioFileReader(path);
                _waveOut = new WaveOutEvent();
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Init(_audioFile);
                _waveOut.Volume = (float)VolumeSlider.Value;

                ProgressSlider.Maximum = _audioFile.TotalTime.TotalSeconds;
                MiniProgressSlider.Maximum = _audioFile.TotalTime.TotalSeconds;
                TotalTimeText.Text = FormatTime(_audioFile.TotalTime);
                MiniTotalTimeText.Text = FormatTime(_audioFile.TotalTime);

                if (seekTo > 0 && seekTo < _audioFile.TotalTime.TotalSeconds)
                {
                    _audioFile.CurrentTime = TimeSpan.FromSeconds(seekTo);
                    ProgressSlider.Value = seekTo; MiniProgressSlider.Value = seekTo;
                    CurrentTimeText.Text = FormatTime(_audioFile.CurrentTime);
                    MiniCurrentTimeText.Text = FormatTime(_audioFile.CurrentTime);
                }
                else
                {
                    ProgressSlider.Value = 0; MiniProgressSlider.Value = 0;
                    CurrentTimeText.Text = "0:00"; MiniCurrentTimeText.Text = "0:00";
                }

                if (autoPlay) { PlayInstant(); } else UpdatePlayIcon(false);
                this.Focus();
                UpdateQueueCurrentState();
                SaveSession();

                if (_qIndex >= 0 && _qIndex < _queueBindable.Count)
                {
                    var currentTrack = _queueBindable[_qIndex];
                    if (QueueList.Items.Contains(currentTrack))
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            QueueList.ScrollIntoView(currentTrack);
                        }), DispatcherPriority.Loaded);
                    }
                }
            }
            catch (Exception ex)
            {
                StopAndDispose();
                UpdatePlayIcon(false);
                MessageBox.Show("Ошибка воспроизведения: " + ex.Message, "Uroborus", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PlayInstant()
        {
            if (_waveOut == null) return;
            _waveOut.Volume = (float)VolumeSlider.Value;
            _waveOut.Play();
            _timer.Start();
            UpdatePlayIcon(true);
        }

        private void PauseInstant()
        {
            if (_waveOut == null || _waveOut.PlaybackState != PlaybackState.Playing) return;
            _waveOut.Pause();
            _timer.Stop();
            UpdatePlayIcon(false);
        }

        private void StopAndDispose()
        {
            _timer.Stop();
            if (_waveOut != null) { _waveOut.PlaybackStopped -= OnPlaybackStopped; _waveOut.Stop(); _waveOut.Dispose(); _waveOut = null; }
            _audioFile?.Dispose(); _audioFile = null;
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Exception != null)
                {
                    UpdatePlayIcon(false);
                    _timer.Stop();
                    MessageBox.Show("Ошибка: " + e.Exception.Message);
                    return;
                }
                if (_isLoop && _audioFile != null) { _audioFile.CurrentTime = TimeSpan.Zero; PlayInstant(); return; }
                if (_queueBindable.Count > 0 && (_isShuffle || _qIndex < _queueBindable.Count - 1))
                {
                    _qIndex = _isShuffle ? _random.Next(0, _queueBindable.Count) : _qIndex + 1;
                    Load(_queueBindable[_qIndex].Path); return;
                }
                UpdatePlayIcon(false); _timer.Stop();
                if (_audioFile != null) _audioFile.CurrentTime = TimeSpan.Zero;
                ProgressSlider.Value = 0; MiniProgressSlider.Value = 0;
                CurrentTimeText.Text = "0:00"; MiniCurrentTimeText.Text = "0:00";
                SaveSession();
            });
        }

        private void SaveSession()
        {
            lock (_ioLock)
            {
                try
                {
                    var session = new SessionData
                    {
                        QueuePaths = [.. _queueBindable.Select(q => q.Path)],
                        QueueIndex = _qIndex,
                        Volume = VolumeSlider.Value,
                        Position = _audioFile?.CurrentTime.TotalSeconds ?? 0,
                        IsLoop = _isLoop,
                        IsShuffle = _isShuffle,
                        WindowLeft = _isMiniMode ? _normalLeft : this.Left,
                        WindowTop = _isMiniMode ? _normalTop : this.Top
                    };
                    string json = JsonSerializer.Serialize(session, _jsonOptions);
                    string tempPath = SessionPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, SessionPath, true);
                }
                catch (Exception ex) { Debug.WriteLine($"SaveSession Error: {ex}"); }
            }
        }

        private void SavePlaylists()
        {
            lock (_ioLock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_playlists, _jsonOptions);
                    string tempPath = PlaylistsPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, PlaylistsPath, true);
                }
                catch (Exception ex) { Debug.WriteLine($"SavePlaylists Error: {ex}"); }
            }
        }

        private void RestoreSession()
        {
            try
            {
                if (!File.Exists(SessionPath)) return;
                var session = JsonSerializer.Deserialize<SessionData>(File.ReadAllText(SessionPath));
                if (session == null) return;

                if (!double.IsNaN(session.WindowLeft) && !double.IsNaN(session.WindowTop))
                    if (session.WindowLeft > -10000 && session.WindowTop > -10000)
                    {
                        this.WindowStartupLocation = WindowStartupLocation.Manual;
                        this.Left = session.WindowLeft; this.Top = session.WindowTop;
                    }

                VolumeSlider.Value = Math.Clamp(session.Volume, 0, 1);
                _isLoop = session.IsLoop; _isShuffle = session.IsShuffle; UpdateModes();

                var validPaths = session.QueuePaths.Where(File.Exists).ToList();

                var itemsToAdd = new List<TrackViewModel>();
                foreach (var path in validPaths)
                {
                    var (dur, info, title) = GetCachedMeta(path);
                    itemsToAdd.Add(new TrackViewModel { Path = path, Title = title, Duration = dur, Info = info, DisplayIndex = itemsToAdd.Count + 1, IsLiked = IsLiked(path) });
                }
                _queueBindable.InsertRange(0, itemsToAdd);

                if (_queueBindable.Count == 0) return;
                _qIndex = Math.Clamp(session.QueueIndex, 0, _queueBindable.Count - 1);

                if (Environment.GetCommandLineArgs().Length > 1) return;

                Load(_queueBindable[_qIndex].Path, autoPlay: false, seekTo: session.Position);
            }
            catch (Exception ex) { Debug.WriteLine($"RestoreSession Error: {ex}"); }
        }

        protected override void OnClosed(EventArgs e)
        {
            _source?.RemoveHook(HwndHook);
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _dragScrollTimer.Stop();
            _trayIcon.Dispose();
            SaveSession();
            StopAndDispose();
            _singleInstanceMutex?.Dispose();
            base.OnClosed(e);
        }

        private void RefreshApp_Click(object sender, RoutedEventArgs e)
        {
            LoadPlaylists();
            if (_drawerOpen) { if (_detailPl != null) RefreshDetailView(); else RefreshPlaylistList(); }
            RefreshQueueSearch();
        }

        private Playlist GetOrCreateLiked()
        {
            var liked = _playlists.FirstOrDefault(p => p.IsLiked);
            if (liked == null) { liked = new Playlist { Name = "Любимые", IsLiked = true }; _playlists.Insert(0, liked); }
            return liked;
        }

        private bool IsLiked(string path) => _playlists.FirstOrDefault(p => p.IsLiked)?.Tracks.Any(t => string.Equals(t, path, StringComparison.OrdinalIgnoreCase)) == true;

        private void ToggleLike(string path)
        {
            var liked = GetOrCreateLiked();

            int existingIdx = liked.Tracks.FindIndex(t => string.Equals(t, path, StringComparison.OrdinalIgnoreCase));
            if (existingIdx >= 0) liked.Tracks.RemoveAt(existingIdx);
            else liked.Tracks.Insert(0, path);

            SavePlaylists();

            bool isNowLiked = IsLiked(path);

            foreach (var item in _queueBindable.Where(q => string.Equals(q.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsLiked = isNowLiked;
            }
            foreach (var item in _detailBindable.Where(q => string.Equals(q.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsLiked = isNowLiked;
            }

            if (_drawerOpen) { if (_detailPl == null) RefreshPlaylistList(); else if (_detailPl.IsLiked) RefreshDetailView(); }
        }

        private void SearchToggleBtn_Click(object sender, RoutedEventArgs e) { if (_searchOpen) CloseSearch(); else OpenSearch(); }

        private void OpenSearch()
        {
            _searchOpen = true;
            SearchBar.Visibility = Visibility.Visible; SearchBar.Opacity = 0;
            SearchTranslate.Y = -10;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            var slide = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };

            SearchBar.BeginAnimation(UIElement.OpacityProperty, fade);
            SearchTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            SearchBoxCtrl.Focus();
            SearchToggleBtn.Foreground = (SolidColorBrush)FindResource("Accent");
        }

        private void CloseSearch()
        {
            _searchOpen = false;
            _searchQuery = ""; SearchBoxCtrl.Text = "";
            SearchToggleBtn.Foreground = (SolidColorBrush)FindResource("TextSec");

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            var slide = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, __) => SearchBar.Visibility = Visibility.Collapsed;

            SearchBar.BeginAnimation(UIElement.OpacityProperty, fade);
            SearchTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            RefreshQueueSearch();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = SearchBoxCtrl.Text.Trim().ToLower();
            RefreshQueueSearch();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseSearch(); this.Focus(); }
        }

        private ContextMenu BuildTrackMenu(TrackViewModel vm)
        {
            string path = vm.Path;
            var cm = new ContextMenu { Style = (Style)FindResource("ModernMenu") };
            MenuItem MakeItem(string header, Action onClick, bool isDanger = false)
            {
                var mi = new MenuItem { Header = header, Style = (Style)FindResource("ModernMenuItem") };
                if (isDanger) mi.Foreground = (SolidColorBrush)FindResource("Accent");
                mi.Click += (_, __) => onClick(); return mi;
            }

            var (_, _, title) = GetCachedMeta(path);
            var titleText = new TextBlock
            {
                Text = title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 250
            };
            var titleItem = new MenuItem
            {
                Header = titleText,
                Style = (Style)FindResource("ModernMenuItem"),
                IsEnabled = false,
                Foreground = (SolidColorBrush)FindResource("TextMain"),
                FontWeight = FontWeights.SemiBold
            };
            cm.Items.Add(titleItem);
            cm.Items.Add(new Separator { Style = (Style)FindResource("ModernSeparator") });

            cm.Items.Add(MakeItem(IsLiked(path) ? "♥  Убрать из Любимых" : "♡  Добавить в Любимые", () => ToggleLike(path)));
            cm.Items.Add(new Separator { Style = (Style)FindResource("ModernSeparator") });
            var nonLiked = _playlists.Where(p => !p.IsLiked).ToList();
            if (nonLiked.Count > 0)
            {
                var addSub = new MenuItem { Header = "Добавить в плейлист", Style = (Style)FindResource("ModernMenuItem") };
                foreach (var pl in nonLiked)
                {
                    var targetPl = pl;
                    var targetPath = path;
                    var mi = new MenuItem { Header = $"  {pl.Name}", Style = (Style)FindResource("ModernMenuItem") };
                    mi.Click += (_, __) =>
                    {
                        int existingIdx = targetPl.Tracks.FindIndex(t => string.Equals(t, targetPath, StringComparison.OrdinalIgnoreCase));
                        if (existingIdx >= 0)
                        {
                            var (dur, info, t) = GetCachedMeta(targetPath);
                            var dlg = new CustomMessageBox("Дубликат", $"Трек «{t}» уже есть в плейлисте «{targetPl.Name}».\nСоздать дубликат?", false) { Owner = this };
                            if (dlg.ShowDialog() != true) return;
                        }

                        targetPl.Tracks.Insert(0, targetPath);
                        SavePlaylists();
                        if (_drawerOpen && _detailPl == targetPl) RefreshDetailView();
                    };
                    addSub.Items.Add(mi);
                }
                cm.Items.Add(addSub);
            }

            cm.Items.Add(MakeItem("✦  Создать плейлист с треком", () =>
            {
                var dlg = new RenameDialog("", "") { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.NewName))
                { var newPl = new Playlist { Name = dlg.NewName.Trim(), ImagePath = dlg.NewImagePath }; newPl.Tracks.Add(path); _playlists.Add(newPl); SavePlaylists(); if (_drawerOpen && _detailPl == null) RefreshPlaylistList(); }
            }));
            cm.Items.Add(new Separator { Style = (Style)FindResource("ModernSeparator") });
            cm.Items.Add(MakeItem("↕  Переместить на позицию...", () =>
            {
                int currentPos = _queueBindable.IndexOf(vm) + 1;
                if (currentPos <= 0) return;
                var dlg = new MoveToPositionDialog(currentPos, _queueBindable.Count) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    int from = currentPos - 1;
                    int to = dlg.TargetPosition - 1;
                    if (from != to) ReorderQueue(from, to);
                }
            }));
            cm.Items.Add(MakeItem("Удалить из очереди", () => RemoveTrack(vm), isDanger: true));
            return cm;
        }

        private void PlaylistsBtn_Click(object sender, RoutedEventArgs e) { if (_drawerOpen) CloseDrawer(); else OpenDrawer(); }
        private void DrawerClose_Click(object sender, RoutedEventArgs e) => CloseDrawer();
        private void DrawerOverlay_Click(object sender, MouseButtonEventArgs e) => CloseDrawer();

        private void OpenDrawer()
        {
            _drawerOpen = true;
            _detailPl = null; ShowPlaylistList();
            DrawerOverlay.Visibility = Visibility.Visible; PlaylistDrawer.Visibility = Visibility.Visible;

            DrawerOverlay.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            DrawerOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
            SlideDrawer(true);
        }

        private void CloseDrawer()
        {
            _drawerOpen = false;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, __) => DrawerOverlay.Visibility = Visibility.Collapsed;
            DrawerOverlay.BeginAnimation(UIElement.OpacityProperty, fade);

            SlideDrawer(false);
        }

        private void SlideDrawer(bool open)
        {
            double from = open ? -320 : 0, to = open ? 0 : -320; int ms = open ? 350 : 250;
            IEasingFunction ease = open ? new QuarticEase { EasingMode = EasingMode.EaseOut } : new QuarticEase { EasingMode = EasingMode.EaseIn };
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            if (!open) anim.Completed += (_, __) => { PlaylistDrawer.Visibility = Visibility.Collapsed; this.Focus(); };
            DrawerTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void ShowPlaylistList()
        {
            _detailPl = null;
            DrawerTitle.Text = "М Е Д И А Т Е КА"; DrawerBackBtn.Visibility = Visibility.Collapsed;
            NewPlaylistBtn.Visibility = Visibility.Visible; PlaylistListScroll.Visibility = Visibility.Visible;
            PlaylistDetailView.Visibility = Visibility.Collapsed; RefreshPlaylistList();
        }

        private void ShowPlaylistDetail(Playlist pl)
        {
            _detailPl = pl;
            DrawerTitle.Text = "П Л Е Й Л И С Т"; DrawerBackBtn.Visibility = Visibility.Visible;
            NewPlaylistBtn.Visibility = Visibility.Collapsed; PlaylistListScroll.Visibility = Visibility.Collapsed;
            PlaylistDetailView.Visibility = Visibility.Visible;
            CloseDetailSearch();

            DetailDeleteBtn.Visibility = pl.IsLiked ? Visibility.Collapsed : Visibility.Visible;
            DetailRenameBtn.Visibility = pl.IsLiked ? Visibility.Collapsed : Visibility.Visible;
            DetailAddBtnToggle.Visibility = pl.IsLiked ? Visibility.Collapsed : Visibility.Visible;
            DetailClearBtn.Visibility = Visibility.Visible;
            DetailPlName.Text = pl.Name; DetailPlIconBorder.Child = null;
            if (pl.IsLiked)
            {
                DetailPlIconBorder.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95));
                DetailPlIconBorder.Child = new TextBlock { Text = "♥", Foreground = Brushes.White, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            else if (!string.IsNullOrEmpty(pl.ImagePath) && File.Exists(pl.ImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(pl.ImagePath); bmp.EndInit(); bmp.Freeze();
                DetailPlIconBorder.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
            }
            else
            {
                DetailPlIconBorder.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                DetailPlIconBorder.Child = new TextBlock { Text = "🎵", FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            RefreshDetailView();
        }

        private void DrawerBack_Click(object sender, RoutedEventArgs e) => ShowPlaylistList();
        private void MiniMarqueeContainer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrackTitle(TrackNameText.Text, TrackSubText.Text);

        private void RefreshPlaylistList()
        {
            PlaylistPanel.Children.Clear();
            var textMain = (SolidColorBrush)FindResource("TextMain");
            var textSec = (SolidColorBrush)FindResource("TextSec");

            if (_playlists.Count == 0)
            {
                PlaylistPanel.Children.Add(new TextBlock { Text = "нет плейлистов", Foreground = textSec, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) });
                return;
            }

            foreach (var pl in _playlists)
            {
                var pl_ref = pl;
                var card = new Border { Background = new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)), CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)) };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var iconBorder = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
                if (pl.IsLiked) { iconBorder.Background = new SolidColorBrush(Color.FromRgb(224, 17, 95)); iconBorder.Child = new TextBlock { Text = "♥", Foreground = Brushes.White, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; }
                else if (!string.IsNullOrEmpty(pl.ImagePath) && File.Exists(pl.ImagePath)) { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(pl.ImagePath); bmp.EndInit(); bmp.Freeze(); iconBorder.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill }; }
                else { iconBorder.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)); iconBorder.Child = new TextBlock { Text = "🎵", Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; }

                Grid.SetColumn(iconBorder, 0);
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                var name = new TextBlock { Text = pl.Name, FontSize = 14, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold, Foreground = textMain, TextTrimming = TextTrimming.CharacterEllipsis };
                int cnt = pl.Tracks.Count(File.Exists);
                var count = new TextBlock { Text = $"{cnt} {PluralTracks(cnt)}", FontSize = 11, FontFamily = new FontFamily("Segoe UI"), Foreground = textSec, Margin = new Thickness(0, 2, 0, 0) };
                textStack.Children.Add(name); textStack.Children.Add(count); Grid.SetColumn(textStack, 1);

                var chev = new TextBlock { Text = "›", FontSize = 18, Foreground = textSec, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(chev, 2);
                g.Children.Add(iconBorder); g.Children.Add(textStack); g.Children.Add(chev); card.Child = g;

                card.MouseEnter += (s, e) => { card.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)); chev.Foreground = textMain; };
                card.MouseLeave += (s, e) => { card.Background = new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)); chev.Foreground = textSec; };
                card.MouseLeftButtonDown += (s, e) => ShowPlaylistDetail(pl_ref);
                PlaylistPanel.Children.Add(card);
            }
        }

        private void RefreshDetailView()
        {
            if (_detailPl == null) return;
            int cnt = _detailPl.Tracks.Count(File.Exists);
            DetailPlCount.Text = $"{cnt} {PluralTracks(cnt)}";
            _detailBindable.Clear();

            var existing = _detailPl.Tracks.Where(File.Exists).ToList();
            var itemsToAdd = new List<TrackViewModel>();

            for (int i = 0; i < existing.Count; i++)
            {
                var path = existing[i];
                var (dur, info, title) = GetCachedMeta(path);

                if (!string.IsNullOrEmpty(_detailSearchQuery))
                {
                    bool matches = title.Contains(_detailSearchQuery, StringComparison.CurrentCultureIgnoreCase) ||
                                   Path.GetFileNameWithoutExtension(path).Contains(_detailSearchQuery, StringComparison.CurrentCultureIgnoreCase);
                    if (!matches) continue;
                }

                itemsToAdd.Add(new TrackViewModel
                {
                    Path = path,
                    Title = title,
                    Duration = dur,
                    Info = info,
                    IsLiked = IsLiked(path),
                    DisplayIndex = i + 1
                });
            }

            _detailBindable.InsertRange(0, itemsToAdd);
            DetailEmptyText.Visibility = _detailBindable.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DetailSearchToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _detailSearchOpen = !_detailSearchOpen;
            if (_detailSearchOpen)
            {
                DetailSearchBar.Visibility = Visibility.Visible;
                DetailSearchBar.Opacity = 0;
                DetailSearchTranslate.Y = -10;

                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
                var slide = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };

                DetailSearchBar.BeginAnimation(UIElement.OpacityProperty, fade);
                DetailSearchTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

                DetailSearchBoxCtrl.Focus();
                DetailSearchToggleBtn.Foreground = (SolidColorBrush)FindResource("Accent");
            }
            else CloseDetailSearch();
        }

        private void CloseDetailSearch()
        {
            _detailSearchOpen = false;
            _detailSearchQuery = ""; DetailSearchBoxCtrl.Text = "";

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            var slide = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, __) => DetailSearchBar.Visibility = Visibility.Collapsed;

            DetailSearchBar.BeginAnimation(UIElement.OpacityProperty, fade);
            DetailSearchTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            if (DetailSearchToggleBtn != null) DetailSearchToggleBtn.Foreground = (SolidColorBrush)FindResource("TextSec");
            RefreshDetailView();
        }

        private void DetailSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _detailSearchQuery = DetailSearchBoxCtrl.Text.Trim().ToLower();
            RefreshDetailView();
        }

        private void DetailClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_detailPl == null) return;
            var dlg = new ConfirmDialog("Очистить плейлист", $"Удалить все треки из плейлиста «{_detailPl.Name}»?\n(Сам плейлист и его обложка останутся)") { Owner = this };
            if (dlg.ShowDialog() == true) { _detailPl.Tracks.Clear(); SavePlaylists(); RefreshDetailView(); }
        }

        private void DetailPlay_Click(object sender, RoutedEventArgs e) { if (_detailPl != null) LoadPlaylistToQueue(_detailPl, 0); }

        private async void DetailAddFile_Click(object sender, RoutedEventArgs e)
        {
            DetailAddBtnToggle.IsChecked = false;
            var dlg = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a", Multiselect = true };
            if (dlg.ShowDialog() == true) await AddToPlaylistAsync(dlg.FileNames);
        }

        private async void DetailAddFolder_Click(object sender, RoutedEventArgs e)
        {
            DetailAddBtnToggle.IsChecked = false;
            var dlg = new OpenFolderDialog { Multiselect = true };
            if (dlg.ShowDialog() == true) await AddToPlaylistAsync(dlg.FolderNames);
        }

        private async Task AddToPlaylistAsync(IEnumerable<string> paths)
        {
            if (_detailPl == null) return;
            try
            {
                ShowLoading("Поиск аудиофайлов...");
                await Task.Delay(50);

                Cursor = Cursors.Wait;
                var files = await Task.Run(() =>
                {
                    var result = new List<string>();
                    foreach (var p in paths)
                    {
                        if (Directory.Exists(p)) { var f = Directory.GetFiles(p, "*.*", SearchOption.AllDirectories); Array.Sort(f, StrCmpLogicalW); result.AddRange(f); }
                        else if (File.Exists(p)) result.Add(p);
                    }
                    return result.Where(f => SupportedExts.Contains(Path.GetExtension(f))).ToList();
                });

                LoadingText.Text = $"Добавление {files.Count} треков...";
                await Task.Delay(50);

                var existingPathsDict = _detailPl.Tracks
                    .Select((path, index) => new { path, index })
                    .GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().index);

                int insertPos = 0;
                bool? applyToAllChoice = null;
                int batchCounter = 0;

                foreach (var f in files)
                {
                    bool shouldAdd = true;

                    if (existingPathsDict.TryGetValue(f, out int existingIdx))
                    {
                        if (applyToAllChoice.HasValue)
                        {
                            shouldAdd = applyToAllChoice.Value;
                        }
                        else
                        {
                            var (dur, info, title) = GetCachedMeta(f);
                            var dlg = new CustomMessageBox("Дубликат", $"Трек «{title}» уже есть в плейлисте под номером {existingIdx + 1}.\nДобавить его еще раз?") { Owner = this };
                            bool dialogResult = dlg.ShowDialog() == true;
                            shouldAdd = dialogResult;

                            if (dlg.ApplyToAll)
                            {
                                applyToAllChoice = dialogResult;
                            }
                        }
                    }

                    if (shouldAdd)
                    {
                        _detailPl.Tracks.Insert(insertPos++, f);
                    }

                    batchCounter++;
                    if (batchCounter % 50 == 0) await Task.Delay(1);
                }
            }
            finally
            {
                Cursor = Cursors.Arrow;
                SavePlaylists(); RefreshDetailView();
                HideLoading();
            }
        }

        private void DetailDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_detailPl == null || _detailPl.IsLiked) return;
            var dlg = new ConfirmDialog("Удалить плейлист", $"Удалить плейлист «{_detailPl.Name}» полностью?") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _playlists.Remove(_detailPl); SavePlaylists(); ShowPlaylistList();
        }

        private void DetailRename_Click(object sender, RoutedEventArgs e)
        {
            if (_detailPl == null || _detailPl.IsLiked) return;
            var dlg = new RenameDialog(_detailPl.Name, _detailPl.ImagePath) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.NewName))
            {
                _detailPl.Name = dlg.NewName.Trim();
                _detailPl.ImagePath = dlg.NewImagePath; SavePlaylists(); ShowPlaylistDetail(_detailPl); RefreshPlaylistList();
            }
        }

        private void NewPlaylistBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new RenameDialog("", "") { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.NewName))
            {
                _playlists.Add(new Playlist { Name = dlg.NewName.Trim(), ImagePath = dlg.NewImagePath });
                SavePlaylists(); RefreshPlaylistList();
            }
        }

        private void LoadPlaylistToQueue(Playlist pl, int startIndex = 0)
        {
            var existing = pl.Tracks.Where(File.Exists).ToList();
            if (existing.Count == 0) { MessageBox.Show("В плейлисте нет доступных треков.", "Uroborus", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            StopAndDispose(); _queueBindable.Clear(); QueueLabel.Text = pl.Name; _currentPlaylistId = pl.Id;

            var itemsToAdd = new List<TrackViewModel>();
            foreach (var path in existing)
            {
                var (dur, info, title) = GetCachedMeta(path);
                itemsToAdd.Add(new TrackViewModel { Path = path, Title = title, Duration = dur, Info = info, IsLiked = IsLiked(path), DisplayIndex = itemsToAdd.Count + 1 });
            }
            _queueBindable.InsertRange(0, itemsToAdd);

            _qIndex = Math.Clamp(startIndex, 0, Math.Max(0, _queueBindable.Count - 1));

            UpdateTrackTitle("No Track", ""); ProgressSlider.Value = 0;
            MiniProgressSlider.Value = 0;
            CurrentTimeText.Text = "0:00"; MiniCurrentTimeText.Text = "0:00"; TotalTimeText.Text = "0:00"; MiniTotalTimeText.Text = "0:00";
            UpdatePlayIcon(false); RefreshQueueSearch(); CloseDrawer();
            if (_queueBindable.Count > 0) Load(_queueBindable[_qIndex].Path, true);
        }

        private void ChangePlaylist(int direction)
        {
            var validPlaylists = _playlists.Where(p => p.Tracks.Any(t => File.Exists(t))).ToList();
            if (validPlaylists.Count == 0) { MessageBox.Show("Нет плейлистов с доступными треками.", "Uroborus", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            int currentIndex = validPlaylists.FindIndex(p => p.Id == _currentPlaylistId);
            int nextIndex;
            if (currentIndex < 0) { nextIndex = direction > 0 ? 0 : validPlaylists.Count - 1; }
            else
            {
                nextIndex = (currentIndex + direction) % validPlaylists.Count;
                if (nextIndex < 0) nextIndex += validPlaylists.Count;
            }

            LoadPlaylistToQueue(validPlaylists[nextIndex], 0);
        }

        private void NextPlaylist_Click(object sender, RoutedEventArgs e) => ChangePlaylist(1);
        private void PrevPlaylist_Click(object sender, RoutedEventArgs e) => ChangePlaylist(-1);

        private void RefreshQueueSearch()
        {
            if (string.IsNullOrEmpty(_searchQuery))
            {
                QueueList.ItemsSource = _queueBindable;
            }
            else
            {
                var filtered = _queueBindable.Where(q => q.Title.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase)).ToList();
                QueueList.ItemsSource = filtered;
            }

            if (QueueList.ItemsSource is List<TrackViewModel> lst)
                QueueEmptyText.Visibility = lst.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            else if (QueueList.ItemsSource is ObservableCollection<TrackViewModel> col)
                QueueEmptyText.Visibility = col.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateQueueCurrentState();
        }

        private void UpdateQueueCurrentState()
        {
            for (int i = 0; i < _queueBindable.Count; i++)
                _queueBindable[i].IsCurrent = (i == _qIndex);
        }

        private void ReorderQueue(int from, int to)
        {
            _queueBindable.Move(from, to);
            if (_qIndex == from) _qIndex = to;
            else if (from < _qIndex && to >= _qIndex) _qIndex--;
            else if (from > _qIndex && to <= _qIndex) _qIndex++;
            for (int i = 0; i < _queueBindable.Count; i++) _queueBindable[i].DisplayIndex = i + 1;
            UpdateQueueCurrentState();
        }

        private void RemoveTrack(TrackViewModel vm)
        {
            int idx = _queueBindable.IndexOf(vm);
            if (idx < 0) return;
            if (idx == _qIndex)
            {
                StopAndDispose();
                UpdateTrackTitle("No Track", "");
                ProgressSlider.Value = 0; MiniProgressSlider.Value = 0;
                CurrentTimeText.Text = "0:00"; MiniCurrentTimeText.Text = "0:00";
                TotalTimeText.Text = "0:00"; MiniTotalTimeText.Text = "0:00";
                UpdatePlayIcon(false); UpdateCover("");
            }
            _queueBindable.RemoveAt(idx);

            // ИСПРАВЛЕНИЕ: Безопасное обновление индекса после удаления
            if (_queueBindable.Count > 0)
            {
                if (idx == _qIndex) _qIndex = Math.Clamp(_qIndex, 0, _queueBindable.Count - 1);
                else if (idx < _qIndex) _qIndex--;
            }
            else _qIndex = -1;

            for (int i = 0; i < _queueBindable.Count; i++) _queueBindable[i].DisplayIndex = i + 1;
            UpdateQueueCurrentState();

            if (!string.IsNullOrEmpty(_searchQuery)) RefreshQueueSearch();
        }

        private void Track_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isDragInProgress) { _isDragInProgress = false; e.Handled = true; return; }
            if (e.ChangedButton == MouseButton.Left && sender is Grid g && g.DataContext is TrackViewModel vm)
            {
                int idx = _queueBindable.IndexOf(vm);
                if (idx >= 0) { _qIndex = idx; Load(_queueBindable[_qIndex].Path); }
                e.Handled = true;
            }
        }

        private void DetailTrack_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && sender is Border b && b.DataContext is TrackViewModel vm && _detailPl != null)
            {
                // Вставляем трек в начало очереди и играем, не трогая остальное
                string path = vm.Path;
                if (!File.Exists(path)) return;

                // Если этот трек уже первый в очереди — просто играем
                if (_queueBindable.Count > 0 && string.Equals(_queueBindable[0].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    _qIndex = 0;
                    Load(path, true);
                    CloseDrawer();
                    e.Handled = true;
                    return;
                }

                // Удаляем дубликат если уже есть в очереди
                var existing = _queueBindable.FirstOrDefault(q => string.Equals(q.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) _queueBindable.Remove(existing);

                var (dur, info, title) = GetCachedMeta(path);
                var newVm = new TrackViewModel { Path = path, Title = title, Duration = dur, Info = info, IsLiked = IsLiked(path) };
                _queueBindable.Insert(0, newVm);

                // Обновляем индексы
                for (int i = 0; i < _queueBindable.Count; i++) _queueBindable[i].DisplayIndex = i + 1;

                _qIndex = 0;
                Load(path, true);
                CloseDrawer();
                RefreshQueueSearch();
                e.Handled = true;
            }
        }

        private void TrackLike_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && sender is TextBlock tb && tb.Tag is string path)
            { ToggleLike(path); e.Handled = true; }
        }

        private void DetailTrackRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string path && _detailPl != null)
            { _detailPl.Tracks.Remove(path); SavePlaylists(); RefreshDetailView(); }
        }

        private void Track_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Grid g && g.DataContext is TrackViewModel vm)
            {
                var menu = BuildTrackMenu(vm);
                menu.PlacementTarget = g;
                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void ListBox_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is ListBox lb)
            {
                Point pos = e.GetPosition(lb);
                if (pos.X < 0 || pos.X > lb.ActualWidth || pos.Y < 0 || pos.Y > lb.ActualHeight)
                { StopDragScroll(); HideDropLine(); }
            }
        }

        private void ListBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (sender is ListBox lb && _activeDragListBox != lb) StartDragScroll(lb);
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void ShowDropLine(Grid grid)
        {
            var line = grid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "DropLine");
            if (line != null)
            {
                if (_currentDropLine != null && _currentDropLine != line) _currentDropLine.Visibility = Visibility.Collapsed;
                line.Visibility = Visibility.Visible; _currentDropLine = line;
            }
        }

        private void HideDropLine()
        {
            if (_currentDropLine != null) { _currentDropLine.Visibility = Visibility.Collapsed; _currentDropLine = null; }
        }

        private void TrackHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (!string.IsNullOrEmpty(_searchQuery)) return;
            if (sender is FrameworkElement fe && fe.DataContext is TrackViewModel vm)
            { _dragStartPos = e.GetPosition(null); _dragItem = vm; e.Handled = true; }
        }

        private void TrackHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPos.HasValue && _dragItem != null)
            {
                Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPos.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPos.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var fe = sender as FrameworkElement;
                    var vm = _dragItem;
                    _dragStartPos = null; _dragItem = null;

                    vm.IsDragging = true; _draggedQueueItem = vm; _isDragInProgress = true;

                    StartDragScroll(QueueList);
                    DragDrop.DoDragDrop(fe, vm, DragDropEffects.Move);

                    vm.IsDragging = false; _draggedQueueItem = null;
                    StopDragScroll(); HideDropLine();
                    Dispatcher.BeginInvoke(new Action(() => _isDragInProgress = false), DispatcherPriority.Input);
                }
            }
            else if (e.LeftButton == MouseButtonState.Released)
            { _dragStartPos = null; _dragItem = null; }
        }

        private void TrackItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackViewModel)))
            { e.Effects = DragDropEffects.Move; if (sender is Grid grid) ShowDropLine(grid); }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            { e.Effects = DragDropEffects.Copy; HideDropLine(); }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void TrackItem_DragLeave(object sender, DragEventArgs e) { e.Handled = true; }

        private void TrackItem_Drop(object sender, DragEventArgs e)
        {
            StopDragScroll(); HideDropLine();
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            if (e.Data.GetData(typeof(TrackViewModel)) is TrackViewModel draggedVm && sender is Grid grid && grid.DataContext is TrackViewModel targetVm)
            {
                int fromIdx = _queueBindable.IndexOf(draggedVm);
                int toIdx = _queueBindable.IndexOf(targetVm);
                if (fromIdx >= 0 && toIdx >= 0 && fromIdx != toIdx) ReorderQueue(fromIdx, toIdx);
            }
            e.Handled = true;
        }

        private void DetailTrackHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (!string.IsNullOrEmpty(_detailSearchQuery)) return;
            if (sender is FrameworkElement fe && fe.DataContext is TrackViewModel vm)
            { _dragStartPos = e.GetPosition(null); _dragItem = vm; e.Handled = true; }
        }

        private void DetailTrackHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPos.HasValue && _dragItem != null)
            {
                Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPos.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPos.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var fe = sender as FrameworkElement;
                    var vm = _dragItem;
                    _dragStartPos = null; _dragItem = null;

                    vm.IsDragging = true; _draggedDetailItem = vm;

                    StartDragScroll(DetailTrackList);
                    DragDrop.DoDragDrop(fe, vm, DragDropEffects.Move);
                    vm.IsDragging = false; _draggedDetailItem = null;
                    StopDragScroll(); HideDropLine();
                }
            }
            else if (e.LeftButton == MouseButtonState.Released)
            { _dragStartPos = null; _dragItem = null; }
        }

        private void DetailTrackItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackViewModel)))
            { e.Effects = DragDropEffects.Move; if (sender is Grid grid) ShowDropLine(grid); }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            { e.Effects = DragDropEffects.Copy; HideDropLine(); }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void DetailTrackItem_DragLeave(object sender, DragEventArgs e) { e.Handled = true; }

        private void DetailTrackItem_Drop(object sender, DragEventArgs e)
        {
            StopDragScroll(); HideDropLine();
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (_detailPl == null) return;

            if (e.Data.GetData(typeof(TrackViewModel)) is TrackViewModel draggedVm &&
                sender is Grid grid && grid.DataContext is TrackViewModel targetVm)
            {
                int fromIdx = draggedVm.DisplayIndex - 1;
                int toIdx = targetVm.DisplayIndex - 1;
                if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx) return;

                var existing = _detailPl.Tracks.Where(File.Exists).ToList();
                if (fromIdx >= existing.Count || toIdx >= existing.Count) return;

                string pathToMove = existing[fromIdx];
                existing.RemoveAt(fromIdx);
                existing.Insert(toIdx, pathToMove);
                _detailPl.Tracks = existing;

                for (int i = 0; i < _detailBindable.Count; i++) _detailBindable[i].DisplayIndex = i + 1;
                SavePlaylists();
                RefreshDetailView();
            }
            e.Handled = true;
        }

        private void TaskbarPlayBtn_Click(object sender, EventArgs e) => PlayPauseBtn_Click(null!, null!);
        private void TaskbarNextBtn_Click(object sender, EventArgs e) => NextBtn_Click(null!, null!);
        private void TaskbarPrevBtn_Click(object sender, EventArgs e) => PrevBtn_Click(null!, null!);

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_waveOut == null)
            {
                if (_queueBindable.Count == 0) return;
                int idx = (_qIndex >= 0 && _qIndex < _queueBindable.Count) ? _qIndex : 0;
                _qIndex = idx; Load(_queueBindable[idx].Path, true); return;
            }
            if (_waveOut.PlaybackState == PlaybackState.Playing) { PauseInstant(); SaveSession(); }
            else { PlayInstant(); }
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_queueBindable.Count == 0) return;
            _qIndex = _isShuffle ? _random.Next(0, _queueBindable.Count) : (_qIndex + 1) % _queueBindable.Count;
            Load(_queueBindable[_qIndex].Path);
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_audioFile != null && _audioFile.CurrentTime.TotalSeconds > 3)
            { _audioFile.CurrentTime = TimeSpan.Zero; ProgressSlider.Value = 0; MiniProgressSlider.Value = 0; return; }
            if (_qIndex > 0) { _qIndex--; Load(_queueBindable[_qIndex].Path); }
            else if (_qIndex == 0 && _queueBindable.Count > 0) Load(_queueBindable[0].Path);
        }

        private void LoopBtn_Click(object sender, RoutedEventArgs e) { _isLoop = !_isLoop; if (_isLoop) _isShuffle = false; UpdateModes(); SaveSession(); }
        private void ShuffleBtn_Click(object sender, RoutedEventArgs e) { _isShuffle = !_isShuffle; if (_isShuffle) _isLoop = false; UpdateModes(); SaveSession(); }

        private void UpdateModes()
        {
            LoopBtn.Foreground = _isLoop ? (SolidColorBrush)FindResource("Accent") : (SolidColorBrush)FindResource("TextSec");
            ShuffleBtn.Foreground = _isShuffle ? (SolidColorBrush)FindResource("Accent") : (SolidColorBrush)FindResource("TextSec");
        }

        private void UpdatePlayIcon(bool playing)
        {
            PlayPauseBtn.ApplyTemplate();
            if (PlayPauseBtn.Template.FindName("PlayIconInternal", PlayPauseBtn) is TextBlock icon)
            { icon.Text = playing ? "⏸" : "▶"; icon.Margin = playing ? new Thickness(0) : new Thickness(3, 0, 0, 0); }

            MiniPlayPauseBtn.ApplyTemplate();
            if (MiniPlayPauseBtn.Template.FindName("MiniPlayIconInternal", MiniPlayPauseBtn) is TextBlock miniIcon)
            { miniIcon.Text = playing ? "⏸" : "▶"; miniIcon.Margin = playing ? new Thickness(0) : new Thickness(2, 0, 0, 0); }

            if (TaskbarPlayBtn != null)
            { TaskbarPlayBtn.Description = playing ? "Пауза" : "Играть"; TaskbarPlayBtn.ImageSource = (ImageSource)FindResource(playing ? "TbPause" : "TbPlay"); }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((_searchOpen && SearchBoxCtrl.IsFocused) || (_detailSearchOpen && DetailSearchBoxCtrl.IsFocused))
            { if (e.Key == Key.Escape) { CloseSearch(); CloseDetailSearch(); e.Handled = true; } return; }

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.M) { MiniPlayerBtn_Click(null!, null!); e.Handled = true; return; }
                if (e.Key == Key.F) { if (_searchOpen) CloseSearch(); else OpenSearch(); e.Handled = true; return; }
                if (e.Key == Key.O) { AddFilesDialog(); e.Handled = true; return; }
            }

            switch (e.Key)
            {
                case Key.Space: PlayPauseBtn_Click(null!, null!); e.Handled = true; break;
                case Key.Right: SeekRelative(5); e.Handled = true; break;
                case Key.Left: SeekRelative(-5); e.Handled = true; break;
                case Key.Up: VolumeSlider.Value = Math.Min(1.0, VolumeSlider.Value + 0.05); e.Handled = true; break;
                case Key.Down: VolumeSlider.Value = Math.Max(0.0, VolumeSlider.Value - 0.05); e.Handled = true; break;
                case Key.PageUp: ChangePlaylist(-1); e.Handled = true; break;
                case Key.PageDown: ChangePlaylist(1); e.Handled = true; break;
                case Key.N: case Key.MediaNextTrack: NextBtn_Click(null!, null!); e.Handled = true; break;
                case Key.B: case Key.MediaPreviousTrack: PrevBtn_Click(null!, null!); e.Handled = true; break;
                case Key.M: ToggleMute(); e.Handled = true; break;
                case Key.L: if (_qIndex >= 0 && _qIndex < _queueBindable.Count) ToggleLike(_queueBindable[_qIndex].Path); e.Handled = true; break;
                case Key.S: ShuffleBtn_Click(null!, null!); e.Handled = true; break;
                case Key.R: LoopBtn_Click(null!, null!); e.Handled = true; break;
                case Key.Escape:
                    if (_drawerOpen) CloseDrawer();
                    else if (_searchOpen) CloseSearch();
                    else if (_hotkeysOpen) ToggleHotkeys();
                    e.Handled = true; break;
            }
        }

        private void SeekRelative(double sec)
        {
            if (_audioFile == null) return;
            double maxTime = Math.Max(0, _audioFile.TotalTime.TotalSeconds - 0.1);
            double t = Math.Clamp(_audioFile.CurrentTime.TotalSeconds + sec, 0, maxTime);
            _audioFile.CurrentTime = TimeSpan.FromSeconds(t);
            ProgressSlider.Value = t; MiniProgressSlider.Value = t;
            CurrentTimeText.Text = FormatTime(_audioFile.CurrentTime);
            MiniCurrentTimeText.Text = FormatTime(_audioFile.CurrentTime);
        }

        private void ToggleMute()
        {
            if (_volumeBeforeMute >= 0) { VolumeSlider.Value = _volumeBeforeMute; _volumeBeforeMute = -1; }
            else { _volumeBeforeMute = VolumeSlider.Value; VolumeSlider.Value = 0; }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
            => VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_waveOut != null) _waveOut.Volume = (float)e.NewValue;
            if (VolPct != null) VolPct.Text = $"{(int)(e.NewValue * 100)}%";
            UpdateVolIcon();
        }

        private void VolIcon_Click(object sender, RoutedEventArgs e) { ToggleMute(); e.Handled = true; }

        private void UpdateVolIcon()
        {
            if (VolIcon == null) return;
            double v = VolumeSlider.Value;
            VolIcon.Text = v == 0 ? "🔇" : v < 0.4 ? "🔈" : v < 0.75 ? "🔉" : "🔊";
        }

        private void HotkeysBtn_Click(object sender, RoutedEventArgs e) => ToggleHotkeys();
        private void HotkeysOverlay_Click(object sender, MouseButtonEventArgs e) => ToggleHotkeys();

        private void ToggleHotkeys()
        {
            _hotkeysOpen = !_hotkeysOpen;
            if (_hotkeysOpen) { HotkeysOverlay.Visibility = Visibility.Visible; HotkeysOverlay.Opacity = 0; Fade(HotkeysOverlay, 0, 1, 200); }
            else
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
                fade.Completed += (_, __) => HotkeysOverlay.Visibility = Visibility.Collapsed; HotkeysOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private void Seek_DragStart(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _isSeekDragging = true;
        private void Seek_DragEnd(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isSeekDragging = false;
            if (_audioFile != null)
            { double val = sender == MiniProgressSlider ? MiniProgressSlider.Value : ProgressSlider.Value; _audioFile.CurrentTime = TimeSpan.FromSeconds(val); }
        }

        private void Seek_MouseDown(object sender, MouseButtonEventArgs e) => _isSeekDragging = true;
        private void Seek_MouseUp(object sender, MouseButtonEventArgs e) => Seek_DragEnd(sender, null!);

        private void Header_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_drawerOpen && !_hotkeysOpen)
                try { DragMove(); } catch { }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void LoadPlaylists()
        {
            try
            {
                if (File.Exists(PlaylistsPath))
                    _playlists = JsonSerializer.Deserialize<List<Playlist>>(File.ReadAllText(PlaylistsPath)) ?? [];
            }
            catch (Exception ex) { Debug.WriteLine($"LoadPlaylists Error: {ex}"); _playlists = []; }
        }

        private static void Fade(UIElement el, double from, double to, int ms)
        {
            el.Opacity = from;
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)));
        }

        private static string PluralTracks(int n)
            => n % 10 == 1 && n % 100 != 11 ? "трек" :
               n % 10 is 2 or 3 or 4 && n % 100 is not (12 or 13 or 14) ? "трека" : "треков";

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0}K";
            else return $"{bytes / 1048576.0:0.0}M";
        }

        private static (string dur, string info, string title) GetTrackMetadataFast(string path)
        {
            string dur = "--:--", info = "ERR", title = Path.GetFileNameWithoutExtension(path);
            try
            {
                using var file = TagLib.File.Create(path);
                if (file.Properties != null && file.Properties.Duration.TotalSeconds > 0)
                    dur = FormatTime(file.Properties.Duration);
                var fi = new FileInfo(path);
                string ext = fi.Extension.Replace(".", "").ToUpper();
                info = $"{ext}·{FormatFileSize(fi.Length)}";

                string t = file.Tag.Title;
                string a = file.Tag.FirstPerformer;
                if (!string.IsNullOrWhiteSpace(t))
                    title = string.IsNullOrWhiteSpace(a) ? t : $"{a} - {t}";

                if (dur == "--:--")
                {
                    using var r = new AudioFileReader(path);
                    dur = FormatTime(r.TotalTime);
                }
            }
            catch
            {
                try
                {
                    using var r = new AudioFileReader(path);
                    dur = FormatTime(r.TotalTime);
                    var fi = new FileInfo(path);
                    string ext = fi.Extension.Replace(".", "").ToUpper();
                    info = $"{ext}·{FormatFileSize(fi.Length)}";
                }
                catch (Exception ex) { Debug.WriteLine($"Meta Fallback Error: {ex}"); }
            }
            return (dur, info, title);
        }

        private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy :
                        e.Data.GetDataPresent(typeof(TrackViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            StopDragScroll(); HideDropLine();

            if (e.Handled) return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Handled = true;
                if ((DateTime.Now - _lastDropTime).TotalMilliseconds < 500) return;
                _lastDropTime = DateTime.Now;

                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (_drawerOpen && _detailPl != null && PlaylistDetailView.Visibility == Visibility.Visible)
                {
                    await AddToPlaylistAsync(files);
                }
                else
                {
                    await AddPathsAsync(files);
                }
            }
        }

        private void Artwork_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; AddFilesDialog(); }
        private void AddBtn_Click(object sender, RoutedEventArgs e) => AddFilesDialog();

        private async void AddFilesDialog()
        {
            var dlg = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a", Multiselect = true };
            if (dlg.ShowDialog(this) == true) await AddPathsAsync(dlg.FileNames);
        }

        private async void AddFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Multiselect = true };
            if (dlg.ShowDialog(this) == true) await AddPathsAsync(dlg.FolderNames);
        }

        private void ScrollToCurrentBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_qIndex < 0 || _qIndex >= _queueBindable.Count) return;

            var current = _queueBindable[_qIndex];

            if (!string.IsNullOrEmpty(_searchQuery))
                CloseSearch();

            QueueList.ItemsSource = _queueBindable;
            QueueList.UpdateLayout();

            var sv = GetScrollViewer(QueueList);
            if (sv == null) { QueueList.ScrollIntoView(current); return; }

            QueueList.ScrollIntoView(current);
            QueueList.UpdateLayout();

            if (QueueList.ItemContainerGenerator.ContainerFromItem(current) is not FrameworkElement item) return;

            var transform = item.TransformToAncestor(QueueList);
            var pos = transform.Transform(new Point(0, 0));
            double targetOffset = sv.VerticalOffset + pos.Y - sv.ActualHeight / 2 + item.ActualHeight / 2;
            targetOffset = Math.Max(0, Math.Min(targetOffset, sv.ScrollableHeight));

            AnimateScroll(sv, sv.VerticalOffset, targetOffset);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (QueueList.ItemContainerGenerator.ContainerFromItem(current) is not FrameworkElement container) return;
                var flash = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(2)
                };
                flash.Completed += (_, __) => container.BeginAnimation(UIElement.OpacityProperty, null);
                container.BeginAnimation(UIElement.OpacityProperty, flash);
            }), DispatcherPriority.Loaded);
        }

        private static void AnimateScroll(ScrollViewer sv, double from, double to)
        {
            double elapsed = 0;
            double duration = 450;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) =>
            {
                elapsed += 16;
                double t = Math.Min(elapsed / duration, 1.0);
                t = t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2;
                sv.ScrollToVerticalOffset(from + (to - from) * t);
                if (elapsed >= duration) timer.Stop();
            };
            timer.Start();
        }
        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ConfirmDialog("Очистка очереди", "Вы уверены, что хотите очистить текущую очередь?") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            StopAndDispose(); _queueBindable.Clear(); _qIndex = -1;
            UpdateTrackTitle("No Track", ""); ProgressSlider.Value = 0; MiniProgressSlider.Value = 0;
            CurrentTimeText.Text = "0:00"; MiniCurrentTimeText.Text = "0:00"; TotalTimeText.Text = "0:00"; MiniTotalTimeText.Text = "0:00";
            UpdatePlayIcon(false); UpdateCover("");
            QueueLabel.Text = "О Ч Е Р Е Д Ь"; _currentPlaylistId = ""; RefreshQueueSearch();
        }

        private async Task AddPathsAsync(IEnumerable<string> paths)
        {
            try
            {
                ShowLoading("Поиск аудиофайлов...");
                await Task.Delay(50);

                Cursor = Cursors.Wait;
                var items = await Task.Run(() =>
                {
                    var files = new List<string>();
                    foreach (var p in paths)
                    {
                        if (Directory.Exists(p)) { var f = Directory.GetFiles(p, "*.*", SearchOption.AllDirectories); Array.Sort(f, StrCmpLogicalW); files.AddRange(f); }
                        else if (File.Exists(p)) files.Add(p);
                    }
                    var result = new List<TrackViewModel>();
                    foreach (var f in files)
                    {
                        if (!SupportedExts.Contains(Path.GetExtension(f))) continue;
                        var (dur, info, title) = GetCachedMeta(f);
                        result.Add(new TrackViewModel { Path = f, Title = title, Duration = dur, Info = info });
                    }
                    return result;
                });

                LoadingText.Text = $"Добавление {items.Count} треков...";
                await Task.Delay(50);

                var existingPathsDict = _queueBindable
                    .Select((q, i) => new { q.Path, Index = i })
                    .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Index);

                int insertIndex = 0;
                int addedCount = 0;
                bool? applyToAllChoice = null;
                int batchCounter = 0;

                var itemsToAdd = new List<TrackViewModel>();

                foreach (var item in items)
                {
                    bool shouldAdd = true;

                    if (existingPathsDict.TryGetValue(item.Path, out int existingIdx))
                    {
                        if (applyToAllChoice.HasValue)
                        {
                            shouldAdd = applyToAllChoice.Value;
                        }
                        else
                        {
                            var dlg = new CustomMessageBox("Дубликат", $"Трек «{item.Title}» уже есть в очереди под номером {existingIdx + 1}.\nДобавить его еще раз?") { Owner = this };
                            bool dialogResult = dlg.ShowDialog() == true;
                            shouldAdd = dialogResult;
                            if (dlg.ApplyToAll)
                            {
                                applyToAllChoice = dialogResult;
                            }
                        }
                    }

                    if (shouldAdd)
                    {
                        item.IsLiked = IsLiked(item.Path);
                        itemsToAdd.Add(item);
                        addedCount++;
                    }

                    batchCounter++;
                    if (batchCounter % 50 == 0) await Task.Delay(1);
                }

                _queueBindable.InsertRange(insertIndex, itemsToAdd);

                if (_qIndex >= 0) _qIndex += addedCount;
                else if (_qIndex < 0 && _queueBindable.Count > 0) _qIndex = 0;

                for (int i = 0; i < _queueBindable.Count; i++) _queueBindable[i].DisplayIndex = i + 1;
            }
            finally
            {
                Cursor = Cursors.Arrow;
                this.Focus();
                RefreshQueueSearch();
                HideLoading();
            }
        }
    }
}