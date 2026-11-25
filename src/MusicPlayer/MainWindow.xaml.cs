using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PlaylistModel> _playlists = [];
    private MediaItem? _copiedMedia;
    private PlaylistModel? CurrentPlaylist;

    public MainWindow()
    {
        InitializeComponent();
        _playlists.Add(new PlaylistModel("默认播放列表"));
        PlaylistList.ItemsSource = _playlists;
        PlaylistList.SelectedIndex = 0;
        UpdateMediaView();
    }

    private void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var playlist = new PlaylistModel($"播放列表 {_playlists.Count + 1}");
        _playlists.Add(playlist);
        PlaylistList.SelectedItem = playlist;
        RenamePlaylistDialog(playlist);
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
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CurrentPlaylist = PlaylistList.SelectedItem as PlaylistModel;
        UpdateMediaView();
    }

    private void AddMedia_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is null) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择媒体文件（视频文件仅导入音频来源）",
            Multiselect = true,
            Filter = "支持的媒体文件|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        foreach (var path in dialog.FileNames)
        {
            if (CurrentPlaylist.Items.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            CurrentPlaylist.Items.Add(new MediaItem(path));
        }
        UpdateMediaView();
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
            item.CardHeight = 132 * scale;
        }
        ZoomValueText.Text = $"{Math.Round(scale * 100)}%";
    }

    private static MediaItem? GetContextMedia(object sender)
    {
        if (sender is not MenuItem menuItem) return null;
        return (menuItem.Parent as ContextMenu)?.PlacementTarget is FrameworkElement element
            ? element.DataContext as MediaItem : null;
    }

    private void MediaPlay_Click(object sender, RoutedEventArgs e) => PlaybackPlaceholder_Click(sender, e);

    private void PlaybackPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "播放引擎将在后续开发阶段接入。当前版本先完成媒体管理与界面基础。", "MusicPlayer",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RevealMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null || !File.Exists(media.FullPath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\\"{media.FullPath}\\"") { UseShellExecute = true });
    }

    private void CopyMedia_Click(object sender, RoutedEventArgs e) => _copiedMedia = GetContextMedia(sender);

    private void PasteMedia_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is null || _copiedMedia is null) return;
        CurrentPlaylist.Items.Add(new MediaItem(_copiedMedia.FullPath, _copiedMedia.Name));
        UpdateMediaView();
    }

    private void RenameMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null) return;
        var dialog = new TextEntryWindow("重命名媒体显示名称", media.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.EntryText))
            media.Name = dialog.EntryText.Trim();
        MediaItems.Items.Refresh();
    }

    private void TrimMedia_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "非破坏性剪辑界面将在波形与音频播放引擎接入后实现。", "编辑 / 剪辑",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RemoveMedia_Click(object sender, RoutedEventArgs e)
    {
        var media = GetContextMedia(sender);
        if (media is null || CurrentPlaylist is null) return;
        CurrentPlaylist.Items.Remove(media); // Remove the playlist entry only; never delete the source file.
        UpdateMediaView();
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
    private double _cardHeight = 132;
    private string _name;

    public MediaItem(string path, string? displayName = null)
    {
        FullPath = path;
        _name = displayName ?? Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        Kind = ext is "MP4" or "MKV" or "MOV" or "AVI" or "WMV" or "WEBM"
            ? $"视频容器 · {ext}（仅音频）" : $"音频 · {ext}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string FullPath { get; }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Kind { get; }
    public double CardWidth { get => _cardWidth; set { _cardWidth = value; OnPropertyChanged(); } }
    public double CardHeight { get => _cardHeight; set { _cardHeight = value; OnPropertyChanged(); } }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
        Background = System.Windows.Media.Brushes.DarkSlateGray;
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
