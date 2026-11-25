# Third-party notices

## FFmpeg

MusicPlayer uses FFmpeg and FFprobe to decode audio streams, including audio tracks inside video containers. The Windows release bundles the LGPL build from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds).

FFmpeg licensing information and source code are available at:
- https://ffmpeg.org/legal.html
- https://ffmpeg.org/download.html

The bundled build is selected from the LGPL variant. The exact build's license and configuration files are included in the release under the `ffmpeg` folder.

## NAudio

MusicPlayer uses NAudio (MIT License) for Windows audio output. Source and license:
- https://github.com/naudio/NAudio
