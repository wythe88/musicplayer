# MusicPlayer

A Windows desktop audio player for live-performance workflows.

The first milestone establishes a reliable WPF desktop foundation and media-list workflow. Video containers are treated as audio sources only; video pictures are never rendered.

## Build

Requirements: Windows, .NET 10 SDK.

```powershell
dotnet build .\src\MusicPlayer\MusicPlayer.csproj -c Release
```

Playback, waveform seeking, and non-destructive trimming will be marked complete only after integration with the real audio pipeline and testing.
