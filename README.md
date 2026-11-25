# MusicPlayer

MusicPlayer is a Windows desktop audio player designed for live-performance workflows.

## First version goals

- Manage multiple named playlists and media cards.
- Persist playlists and media entries locally between launches.
- Play common audio formats through FFmpeg decoding and NAudio/Windows audio output.
- Treat video containers as audio sources only: decode the selected audio stream and never render video.
- Generate a waveform for the currently playing track.
- Show a playhead that follows playback; click or drag on the waveform to seek.
- Store trim in/out points non-destructively; the source media file is never modified.
- Copy media entries between playlists, rename display names, reveal source files, and remove entries without deleting source files.

## Build locally

Requirements: Windows and .NET 10 SDK.

```powershell
dotnet build .\\src\\MusicPlayer\\MusicPlayer.csproj -c Release
```

For playback and waveform decoding, place FFmpeg and FFprobe in ffmpeg\\bin next to the application, or make them available on PATH.

## Download

Windows release packages are published on [GitHub Releases](https://github.com/wythe88/musicplayer/releases). Each release ZIP is intended to be extracted and run without separately installing the .NET runtime; FFmpeg/FFprobe are bundled.

## Local data

The playlist library is stored under %LocalAppData%\\MusicPlayer\\library.json.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for FFmpeg and NAudio notices.
