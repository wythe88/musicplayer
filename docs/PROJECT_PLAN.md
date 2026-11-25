# MusicPlayer v0.1 project plan

## Product direction

A Windows desktop audio player for personal live-performance use, taking interaction and layout cues from Hirender P1 without attempting to clone every feature.

## Confirmed interaction requirements

- Playlist management on the left.
- Media displayed as individually identifiable cards in the main workspace.
- Create and rename playlists.
- Import audio files and video containers as audio sources. Video pictures must never be displayed or rendered.
- Copy and paste media entries between playlists without duplicating the underlying media file.
- Context menu: play, reveal source file, copy, paste, rename, edit/trim, remove media.
- Removing media removes the entry from the playlist only; it must not delete the original file.
- A bottom-right slider scales media cards larger or smaller.
- A waveform display is intended to support click-to-seek and drag-to-seek.
- Editing/trimming is intended to use non-destructive start/end markers.

## v0.1 implementation sequence

1. Establish the WPF application structure and Windows CI build.
2. Implement playlist and media-card management.
3. Implement card zoom and context-menu workflows.
4. Integrate audio decoding/output, then generate waveform data and connect seeking.
5. Add non-destructive trim preview and persistence.
6. Test with representative audio files and video containers that contain audio.

## Reliability constraints

- Never delete source media when removing a playlist entry.
- Do not claim playback, waveform seeking, or trimming works until those features are connected to the real audio pipeline and tested.
- Keep UI responsiveness independent from media decoding and waveform analysis.
- Keep video rendering out of scope.
