using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PlaylistModel> _playlists = [];
    private readonly AudioPlaybackService _audio = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly string _libraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicPlayer", "library.json");
    private MediaItem? _copiedMedia;
    private MediaItem? _selectedMedia;
    private MediaItem? _currentMedia;
    private double[] _waveform = [];
    private bool _draggingPlayhead;
    private CancellationTokenSource? _waveformCancellation;
    private PlaylistModel? CurrentPlaylist;

    public MainWindow()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _positionTimer.Tick += PositionTimer_Tick;
        _audio.PlaybackStopped += Audio_PlaybackStopped;

        LoadLibrary();
        PlaylistList.ItemsSource = _playlists;
        if (_playlists.Count == 0) _playlists.Add(new PlaylistModel("默认播放列表"));
        PlaylistList.SelectedIndex = 0;
        UpdateMediaView();
        VolumeSlider_ValueChanged(this, null!);
    }

    private void LoadLibrary()
    {
        try
        {
            if (!File.Exists(_libraryPath)) return;
            var data = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(_libraryPath));
            if (data is null) return;
            foreach (var saved in data.Playlists)
            {
                var playlist = new PlaylistModel(saved.Name);
                foreach (var item in saved.Items)
                    if (!string.IsNullOrWhiteSpace(item.FullPath))
                        playlist.Items.Add(new MediaItem(item.FullPath, item.Name)
                        {
                            DurationSeconds = item.DurationSeconds,
                            TrimStartSeconds = item.TrimStartSeconds,
                            TrimEndSeconds = item.TrimEndSeconds
                        });
                _playlists.Add(playlist);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取媒体库失败：{ex.Message}", "MusicPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveLibrary()
    {
        try
        {
            var folder = Path.GetDirectoryName(_libraryPath)!;
            Directory.CreateDirectory(folder);
            var data = new LibraryData
            {
                Playlists = _playlists.Select(p => new SavedPlaylist
                {
                    Name = p.Name,
                    Items = p.Items.Select(m => new SavedMedia
                    {
                        FullPath = m.FullPath,
                        Name = m.Name,
                        DurationSeconds = m.DurationSeconds,
                        TrimStartSeconds = m.TrimStartSeconds,
                        TrimEndSeconds = m.TrimEndSeconds
                    }).ToList()
                }).ToList()
            };
            var temp = _libraryPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _libraryPath, true);
        }
        catch (Exception ex)
        {
            PlaybackStatusText.Text = $"媒体库保存失败：{ex.Message}";
        }
    }

    private void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var playlist = new PlaylistModel($"播放列表 {_playlists.Count + 1}");
        _playlists.Add(playlist);
        PlaylistList.SelectedItem = playlist;
        RenamePlaylistDialog(playlist);
        SaveLibrary();
    }

    private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistModel playlist)
            RenamePlaylistDialog(playlist);
    }

    private void RenamePlaylistDialog(PlaylistModel playlist)
    {
        var dialog = new TextEntryWindow("重命名播放列表", playlist.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.EntryText))
            playlist.Name = dialog.EntryText.Trim();
        PlaylistList.Items.Refresh();
        CurrentPlaylistTitle.Text = playlist.Name;
        SaveLibrary();
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CurrentPlaylist = PlaylistList.SelectedItem as PlaylistModel;
        UpdateMediaView();
    }

    private async void AddMedia_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is null) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择媒体文件（视频文件只读取音频轨道）",
            Multiselect = true,
            Filter = "支持的媒体文件|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.aiff;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.m4v;*.ts;*.mpeg;*.mpg|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var added = new List<MediaItem>();
        foreach (var path in dialog.FileNames)
        {
            if (CurrentPlaylist.Items.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var item = new MediaItem(path);
            CurrentPlaylist.Items.Add(item);
            added.Add(item);
        }
        UpdateMediaView();
        SaveLibrary();
        foreach (var item in added)
            await LoadMediaMetadataAsync(item);
    }

    private async Task LoadMediaMetadataAsync(MediaItem item)
    {
        try
        {
            var duration = await AudioPlaybackService.GetDurationAsync(item.FullPath);
            item.DurationSeconds = duration.TotalSeconds;
            SaveLibrary();
        }
        catch (Exception ex)
        {
            PlaybackStatusText.Text = $"读取媒体信息失败：{Path.GetFileName(item.FullPath)}";
            Debug.WriteLine(ex);
        }
    }

    private void UpdateMediaView()
    {
        if (CurrentPlaylist is null)
        {
            MediaItems.ItemsSource = null;
            CurrentPlaylistTitle.Text = "媒体";
            MediaCountText.Text = "0 个媒体";
            return;
        }
        CurrentPlaylistTitle.Text = CurrentPlaylist.Name;
        MediaItems.ItemsSource = CurrentPlaylist.Items;
        MediaCountText.Text = $"{CurrentPlaylist.Items.Count} 个媒体";
        ApplyCardZoom();
    }

    private void CardZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyCardZoom();

    private void ApplyCardZoom()
    {
        if (MediaItems is null || CardZoomSlider is null || ZoomValueText is null) return;
        var scale = 0.72 + CardZoomSlider.Value / 100.0 * 0.76;
        foreach (var item in _playlists.SelectMany(x => x.Items))
        {
            item.CardWidth = 150 * scale;
            item.CardHeight = 150 * scale;
        }
        ZoomValueText.Text = $"{Math.Round(scale * 100)}%";
    }

    private static MediaItem? GetContextMedia(object sender)
    {
        if (sender is not MenuItem menuItem) return null;
        return (menuItem.Parent as ContextMenu)?.PlacementTarget is FrameworkElement element
            ? element.DataContext as MediaItem : null;
    }

    private void MediaCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: MediaItem media })
        {
            _selectedMedia = media;
            StartPlayback(media);
            e.Handled = true;
        }
    }

    private void MediaPlay_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is not null) StartPlayback(media);
    }

    private async void StartPlayback(MediaItem media, TimeSpan? startAt = null)
    {
        if (!File.Exists(media.FullPath))
        {
            MessageBox.Show(this, "找不到媒体文件，可能已经被移动或删除。", "MusicPlayer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _currentMedia = media;
            _selectedMedia = media;
            CurrentTrackText.Text = media.Name;
            DurationText.Text = FormatTime(TimeSpan.FromSeconds(media.DurationSeconds));
            CurrentTimeText.Text = "00:00";
            PlaybackStatusText.Text = "正在启动解码器…";
            _waveformCancellation?.Cancel();
            _waveformCancellation = new CancellationTokenSource();
            _waveform = [];
            DrawWaveform();

            var position = startAt ?? TimeSpan.FromSeconds(media.TrimStartSeconds);
            var clipEnd = media.TrimEndSeconds > 0 ? media.TrimEndSeconds : media.DurationSeconds;
            TimeSpan? clipDuration = clipEnd > position.TotalSeconds
                ? TimeSpan.FromSeconds(clipEnd - position.TotalSeconds) : null;
            _audio.Play(media.FullPath, position, clipDuration);
            _positionTimer.Start();
            PlaybackStatusText.Text = "正在播放";
            _ = LoadWaveformAsync(media, _waveformCancellation.Token);
            if (media.DurationSeconds <= 0)
                await LoadMediaMetadataAsync(media);
        }
        catch (Exception ex)
        {
            _positionTimer.Stop();
            PlaybackStatusText.Text = "播放失败";
            MessageBox.Show(this, $"无法播放该媒体：\n{ex.Message}\n\n请确认程序目录包含 FFmpeg。", "MusicPlayer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadWaveformAsync(MediaItem media, CancellationToken token)
    {
        try
        {
            var peaks = await _audio.BuildWaveformAsync(media.FullPath, cancellationToken: token);
            if (token.IsCancellationRequested || _currentMedia != media) return;
            _waveform = peaks;
            DrawWaveform();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (_currentMedia == media) PlaybackStatusText.Text = "正在播放 · 波形生成失败";
        }
    }

    private void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        var media = _selectedMedia ?? CurrentPlaylist?.Items.FirstOrDefault();
        if (media is null) return;
        if (_currentMedia == media && _audio.IsPaused)
            _audio.Resume();
        else
            StartPlayback(media);
        _positionTimer.Start();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _audio.Pause();
        PlaybackStatusText.Text = "已暂停";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop();
        _positionTimer.Stop();
        CurrentTimeText.Text = "00:00";
        MovePlayhead(0);
        PlaybackStatusText.Text = "已停止";
    }

    private void Audio_PlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_audio.Position.TotalSeconds > 0 && _currentMedia is not null)
                PlaybackStatusText.Text = "播放结束";
            _positionTimer.Stop();
        });
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentMedia is null || _draggingPlayhead) return;
        var position = _audio.Position;
        CurrentTimeText.Text = FormatTime(position);
        var duration = _currentMedia.DurationSeconds;
        if (duration > 0) MovePlayhead(position.TotalSeconds / duration);
    }

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawWaveform();

    private void DrawWaveform()
    {
        if (WaveformCanvas is null) return;
        WaveformCanvas.Children.Clear();
        var width = WaveformCanvas.ActualWidth;
        var height = WaveformCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        if (_waveform.Length == 0)
        {
            var hint = new TextBlock
            {
                Text = _currentMedia is null ? "播放音乐后，这里显示当前曲目的实时波形" : "正在分析音频波形…",
                Foreground = (Brush)FindResource("MutedText"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(hint, Math.Max(8, (width - 300) / 2));
            Canvas.SetTop(hint, height / 2 - 10);
            WaveformCanvas.Children.Add(hint);
            return;
        }

        var slot = width / _waveform.Length;
        var barWidth = Math.Max(1, slot * 0.66);
        for (var i = 0; i < _waveform.Length; i++)
        {
            var barHeight = Math.Max(2, _waveform[i] * (height * 0.82));
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 1,
                RadiusY = 1,
                Fill = (Brush)FindResource("AccentBrush"),
                Opacity = 0.88
            };
            Canvas.SetLeft(bar, i * slot);
            Canvas.SetTop(bar, (height - barHeight) / 2);
            WaveformCanvas.Children.Add(bar);
        }
        var playhead = new Rectangle
        {
            Tag = "playhead",
            Width = 2,
            Height = height,
            Fill = Brushes.White,
            Opacity = 0.95
        };
        Canvas.SetLeft(playhead, 0);
        WaveformCanvas.Children.Add(playhead);
    }

    private void MovePlayhead(double ratio)
    {
        if (WaveformCanvas is null) return;
        var line = WaveformCanvas.Children.OfType<Rectangle>().LastOrDefault(x => Equals(x.Tag, "playhead"));
        if (line is null) return;
        Canvas.SetLeft(line, Math.Clamp(ratio, 0, 1) * Math.Max(0, WaveformCanvas.ActualWidth - line.Width));
    }

    private void Waveform_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentMedia is null || _currentMedia.DurationSeconds <= 0) return;
        _draggingPlayhead = true;
        WaveformCanvas.CaptureMouse();
        UpdateSeekPreview(e.GetPosition(WaveformCanvas).X);
    }

    private void Waveform_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingPlayhead) UpdateSeekPreview(e.GetPosition(WaveformCanvas).X);
    }

    private void Waveform_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingPlayhead || _currentMedia is null) return;
        var ratio = Math.Clamp(e.GetPosition(WaveformCanvas).X / Math.Max(1, WaveformCanvas.ActualWidth), 0, 1);
        _draggingPlayhead = false;
        WaveformCanvas.ReleaseMouseCapture();
        StartPlayback(_currentMedia, TimeSpan.FromSeconds(_currentMedia.DurationSeconds * ratio));
    }

    private void UpdateSeekPreview(double x)
    {
        if (_currentMedia is null) return;
        var ratio = Math.Clamp(x / Math.Max(1, WaveformCanvas.ActualWidth), 0, 1);
        MovePlayhead(ratio);
        CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(_currentMedia.DurationSeconds * ratio));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audio is not null) _audio.Volume = (float)(VolumeSlider.Value / 100.0);
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private void RevealMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null || !File.Exists(media.FullPath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{media.FullPath}\"") { UseShellExecute = true });
    }

    private void CopyMedia_Click(object sender, RoutedEventArgs e) => _copiedMedia = GetContextMedia(sender);

    private async void PasteMedia_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is null || _copiedMedia is null) return;
        var item = new MediaItem(_copiedMedia.FullPath, _copiedMedia.Name);
        CurrentPlaylist.Items.Add(item);
        UpdateMediaView();
        SaveLibrary();
        await LoadMediaMetadataAsync(item);
    }

    private void RenameMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null) return;
        var dialog = new TextEntryWindow("重命名媒体显示名称", media.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.EntryText))
            media.Name = dialog.EntryText.Trim();
        MediaItems.Items.Refresh();
        if (_currentMedia == media) CurrentTrackText.Text = media.Name;
        SaveLibrary();
    }

    private void TrimMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null) return;
        var dialog = new TrimEntryWindow(media.TrimStartSeconds, media.TrimEndSeconds, media.DurationSeconds) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        media.TrimStartSeconds = dialog.StartSeconds;
        media.TrimEndSeconds = dialog.EndSeconds;
        SaveLibrary();
        if (_currentMedia == media)
            StartPlayback(media, TimeSpan.FromSeconds(media.TrimStartSeconds));
    }

    private void RemoveMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null || CurrentPlaylist is null) return;
        if (_currentMedia == media) Stop_Click(sender, e);
        CurrentPlaylist.Items.Remove(media);
        UpdateMediaView();
        SaveLibrary();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _waveformCancellation?.Cancel();
        _positionTimer.Stop();
        _audio.Dispose();
        SaveLibrary();
    }
}

public sealed class PlaylistModel(string name)
{
    public string Name { get; set; } = name;
    public ObservableCollection<MediaItem> Items { get; } = [];
}

public sealed class MediaItem : INotifyPropertyChanged
{
    private double _cardWidth = 150;
    private double _cardHeight = 150;
    private string _name;
    private double _durationSeconds;
    private double _trimStartSeconds;
    private double _trimEndSeconds;

    public MediaItem(string path, string? displayName = null)
    {
        FullPath = path;
        _name = displayName ?? Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        Kind = ext is "MP4" or "MKV" or "MOV" or "AVI" or "WMV" or "WEBM" or "M4V" or "TS" or "MPEG" or "MPG"
            ? $"视频容器 · {ext}（仅音频）" : $"音频 · {ext}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string FullPath { get; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Kind { get; }
    public double DurationSeconds { get => _durationSeconds; set { _durationSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); } }
    public string DurationText => DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds).ToString(@"m\:ss") : "读取时长中…";
    public double TrimStartSeconds { get => _trimStartSeconds; set { _trimStartSeconds = value; OnPropertyChanged(); } }
    public double TrimEndSeconds { get => _trimEndSeconds; set { _trimEndSeconds = value; OnPropertyChanged(); } }
    public double CardWidth { get => _cardWidth; set { _cardWidth = value; OnPropertyChanged(); } }
    public double CardHeight { get => _cardHeight; set { _cardHeight = value; OnPropertyChanged(); } }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TrimEntryWindow : Window
{
    private readonly TextBox _start;
    private readonly TextBox _end;
    public double StartSeconds => double.Parse(_start.Text, System.Globalization.CultureInfo.InvariantCulture);
    public double EndSeconds => double.Parse(_end.Text, System.Globalization.CultureInfo.InvariantCulture);

    public TrimEntryWindow(double start, double end, double duration)
    {
        Title = "非破坏性剪辑";
        Width = 410;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)new BrushConverter().ConvertFromString("#191F29")!;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = $"设置播放片段范围（原文件不会被修改，时长 {TimeSpan.FromSeconds(Math.Max(0, duration)):m\\:ss}）", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition());
        _start = new TextBox { Text = start.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), Margin = new Thickness(0, 0, 8, 12), Padding = new Thickness(6) };
        _end = new TextBox { Text = end > 0 ? end.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : duration.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), Margin = new Thickness(8, 0, 0, 12), Padding = new Thickness(6) };
        Grid.SetColumn(_end, 1);
        row.Children.Add(_start);
        row.Children.Add(_end);
        panel.Children.Add(row);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "保存片段", IsDefault = true, MinWidth = 85 };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70 };
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(_start.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ||
                !double.TryParse(_end.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ||
                s < 0 || t <= s || (duration > 0 && t > duration))
            {
                MessageBox.Show(this, "请输入有效的起止秒数，结束时间必须大于起始时间且不能超过总时长。");
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

public sealed class TextEntryWindow : Window
{
    private readonly TextBox _textBox;
    public string EntryText => _textBox.Text;

    public TextEntryWindow(string title, string initialText)
    {
        Title = title;
        Width = 380;
        Height = 145;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.DarkSlateGray;
        var panel = new StackPanel { Margin = new Thickness(14) };
        _textBox = new TextBox { Text = initialText, Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "确定", IsDefault = true, MinWidth = 70 };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70 };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

public sealed class LibraryData
{
    public List<SavedPlaylist> Playlists { get; set; } = [];
}
public sealed class SavedPlaylist
{
    public string Name { get; set; } = "播放列表";
    public List<SavedMedia> Items { get; set; } = [];
}
public sealed class SavedMedia
{
    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
    public double DurationSeconds { get; set; }
    public double TrimStartSeconds { get; set; }
    public double TrimEndSeconds { get; set; }
}
