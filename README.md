# WaxIPTV (WPF Skeleton)

**WaxIPTV** is a minimal but extensible IPTV player shell written in WPF for
Windows.  It aims to provide a clean starting point for a desktop IPTV
application that relies on an external media player (mpv or VLC) for
playback, parses standard M3U playlists and XMLTV EPG data, and exposes a
simple theming system via JSON files.  The project targets **.NET 8** and
contains only a few third‑party dependencies for MVVM support and JSON
handling.

## Key Points

- **Platform:** WPF on Windows.  You will need the .NET 8 SDK and an IDE
  such as Visual Studio 2022 to build and run the project.
- **External players:** The app launches either **mpv** or **VLC** and
  controls it via their respective IPC mechanisms (mpv JSON‑IPC over a
  named pipe; VLC’s RC interface over TCP).  The choice of player and
  executable paths are defined in `settings.json`.
- **Playlist and EPG:** M3U (including M3U8) playlist files are parsed
  into a list of channels.  XMLTV feeds are loaded on a schedule, mapped
  to channels using `tvg-id` or normalised names, and held in memory for
  a few days.  A helper method computes the current and next programme for
  a channel.
- **Theming and layout:** A simple theming system reads colour, typography
  and shape tokens from `theme.json` and applies them at runtime via a
  helper (`ThemeLoader`).  A separate `layout.json` file hints which
  controls belong on certain screens.  Both files can be hot‑reloaded.
- **Skeleton UI:** The main window hosts a channel list, a panel
  displaying the "Now/Next" programme, and buttons to play/pause/stop
  playback.  Additional views (`ChannelListView`, `EpgNowNext`,
  `SettingsView`) are provided for composition.

## Getting Started

1. **Install the .NET 8 SDK** from [dotnet.microsoft.com](https://dotnet.microsoft.com/download) and a suitable IDE such as Visual Studio 2022.
2. Clone or download this repository and open the solution in Visual Studio by opening **WaxIPTV.csproj** (or run `dotnet build` in the project root).  NuGet packages will be restored automatically.
3. Build and run the project.  On first launch, the app will attempt to detect installed copies of mpv and VLC and write a `settings.json` file into the application directory with defaults.  You can edit this file to specify:
   - `player`: `"mpv"` or `"vlc"`.
   - `mpvPath`/`vlcPath`: absolute paths to the players’ executables.
   - `playlistUrl`: a local path or URL to your M3U playlist.
   - `xmltvUrl`: a path or URL to your XMLTV EPG. If left blank, the application will prompt for an EPG link on startup.
   - `epgRefreshHours`: how often, in hours, to reload the EPG.
4. Place your playlist (`.m3u` or `.m3u8`) somewhere accessible. On first run the app will ask for an EPG URL, download the XML (supporting `.gz` compression) and cache it under your `%LocalAppData%\WaxIPTV` folder. Subsequent launches read from this cached file unless the refresh interval has elapsed. The channel list will be populated from the playlist and the Now/Next panel will show programme information from the cached EPG.
5. Click **Play**, **Pause** and **Stop** to control the chosen external player.  If no player is available (paths are blank and detection fails), the playback buttons will be disabled.

### Customising the Theme and Layout

The look and feel of the application can be controlled by editing the JSON files in the project root:

- **theme.json** defines colours, typography and shape tokens.  For example:

  ```json
  {
    "colors": {
      "bg": "#0B0B0F",
      "text": "#FFFFFF",
      "accent": "#5B8CFF"
    },
    "typography": {
      "fontFamily": "Segoe UI",
      "sizeBase": 14
    },
    "shape": {
      "radius": 8
    }
  }
  ```

  The `ThemeLoader` class reads this JSON and applies it to a WPF
  `ResourceDictionary`.  You can call `ThemeLoader.ApplyThemeJson` at
  runtime with new JSON to hot‑reload a theme (for example, from a
  settings window).

- **layout.json** provides high‑level hints about which components belong
  on particular screens.  The skeleton doesn’t yet interpret this file
  beyond storing it; you are free to extend it.  An example layout
  configuration might look like:

  ```json
  {
    "home": ["search", "groups", "favorites"],
    "playerBar": { "buttons": ["pause", "stop", "volume"] }
  }
  ```

### EPG and Now/Next

Programmes are parsed from the XMLTV feed and stored in UTC.  The
`EpgHelpers.GetNowNext` method computes the current and next programmes by
comparing the current UTC time against programme start/end times.  The
`EpgNowNext` user control converts these times to local time when
displaying them.  Only a few days of EPG data are retained in memory.

## Configuration and Player Detection

The `SettingsService` loads configuration from `settings.json` on startup
and provides fallback detection when no configuration file is present:

- **mpv** is searched via the `where` command and common installation
  paths such as `C:\\Program Files\\mpv\\mpv.exe`.
- **VLC** is detected by querying the Windows registry for the
  `VideoLAN\\VLC` installation directory.

You can override detected paths by editing `settings.json`.  When you
persist settings (by calling `SettingsService.Save()` from a settings
panel, for example), the current values are written back to the file.

## Packaging to a Single .exe

To create a self‑contained executable, ensure your project file
(`WaxIPTV.csproj`) includes the following properties:

```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <PublishTrimmed>true</PublishTrimmed>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
</PropertyGroup>
```

Publish the application in Release mode:

```bash
dotnet publish -c Release
```

The resulting binary in `bin/Release/net8.0-windows/win-x64/publish` will be a single
`WaxIPTV.exe` with all dependencies bundled.  For production use you may wish to
code‑sign the executable and wrap it in an installer (WiX, NSIS) to avoid
SmartScreen warnings on Windows.

## File Layout

The repository is organised into several directories to separate concerns:

| Folder             | Purpose |
|--------------------|---------|
| **Models/**        | Plain‑old CLR objects (`Channel`, `Programme`, `AppSettings`) used throughout the app. |
| **Services/**      | Helpers for parsing playlists (`M3UParser`), loading and mapping EPG data (`Xmltv`, `EpgMapper`, `EpgHelpers`), controlling external players (`MpvController`, `VlcController`), and reading/writing settings (`SettingsService`). |
| **Theming/**       | The `ThemeLoader` class applies JSON themes to WPF resource dictionaries; place additional theming helpers here. |
| **Views/**         | XAML user controls and windows (`MainWindow`, `ChannelListView`, `EpgNowNext`, `SettingsView`).  `MainWindow` assembles these pieces into the overall UI. |
| Root JSON files    | `settings.json` contains user preferences; `theme.json` defines colours and fonts; `layout.json` is a placeholder for layout hints. |

Feel free to expand upon this skeleton by introducing your own MVVM layers,
database persistence, additional playback features (timeshift, catch‑up),
and more sophisticated theming and layout configuration.  The existing
structure is intended to mirror the phased development approach described
in the accompanying plan, providing a clear roadmap for building a fully
featured IPTV application.