# WaxIPTV (WPF Skeleton)

This project provides the minimal skeleton for a Windows IPTV player named **WaxIPTV** using WPF and an external media player (e.g., mpv or VLC). It targets **.NET 8.0** and references common packages.

## Key Points

- **Platform:** WPF on Windows. Requires the .NET 8 SDK installed.
- **Packages:**
  - `CommunityToolkit.Mvvm` – Implements the MVVM pattern and source generators.
  - `System.Text.Json` – For JSON parsing and serialization.
  - `Microsoft.Data.Sqlite` – (optional) Light-weight embedded database for local persistence. Remove from the project file if not needed.
- **Folder Structure:**
  - **Models/** – Place model classes here.
  - **Services/** – Add services for parsing playlists, EPG loading, and player control.
  - **Views/** – Contains the main `MainWindow` XAML and code-behind.
  - **Theming/** – Store theme and layout JSON files and helper classes.

## Getting Started

1. **Install the .NET 8 SDK** and a suitable IDE like Visual Studio 2022.
2. Open the solution by running `dotnet build` in this folder or by opening `IptvShell.csproj` in Visual Studio.
3. Restore NuGet packages and build the project. You may remove the `Microsoft.Data.Sqlite` package if you don't plan to use a local database.
4. Start implementing your IPTV player:
   - Parse M3U playlists and XMLTV EPG data in **Services**.
   - Bind data to the UI using MVVM patterns with `CommunityToolkit.Mvvm`.
   - Launch and control an external player (mpv/VLC) through named pipes or TCP.
   - Implement a theming system using JSON files placed in **Theming**.

## Configuration and player detection

At runtime the application reads configuration from a `settings.json` file in the project
root. The file includes the preferred player (`"mpv"` or `"vlc"`), optional paths
to the player executables, links to your M3U playlist and XMLTV EPG, and the refresh
interval for EPG updates. If `settings.json` is missing, the `SettingsService` will
attempt to detect an installed copy of **VLC** by querying the Windows registry for
`VideoLAN\VLC` and fall back to a default installation path. It will also search for
**mpv** via the `where` command and common directories like `C:\Program Files\mpv\mpv.exe`.
Detected paths are saved back to `settings.json` when you persist settings.  You can
edit `settings.json` manually to customise the player selection or override the
auto‑detected locations.

