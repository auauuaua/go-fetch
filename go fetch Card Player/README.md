# go fetch Card Player
Currently windows only software meant to be run concurrently with go fetch Card Player hardware to have hands on access to physical media. The software has two main parts:

1. Tray application - launching the exe starts a tray app that just shows in the windows system tray. This is meant to be run in the background, it connects to the hardware over usb and receives serial messages to launch user set media and software based on the QR code read by the hardware. Can set this to launch at startup through Windows. 
a system that plays media by scanning QR code cards with a custom serial card reader.

2. Editor window - accessible by launching the exe a second time or right clicking on the tray icon and clicking open editor. This is the interface to link and set up your media, players, cards, remotes, and generate card images to print.

Clicking close on the editor window just closes the editor. To fully shut down the program, right click on the tray icon and click exit. 

---

## Requirements

- Windows 10/11
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or SDK to build)

## Build & Publish

```powershell
cd CardPlayer
dotnet restore
dotnet run                  # run from source

# Publish as a single self-contained exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# Output: bin\Release\net8.0-windows\win-x64\publish\
```

---

## Data Directory

All configuration files are read from and written to:

```
%LOCALAPPDATA%\go fetch\go fetch Card Player
```

On first run the app creates this folder and any missing files with default content.

| File | Description |
|------|-------------|
| `Players.json` | Media type definitions — programs, functions, remote mappings |
| `Cards.csv` | Media library — QR codes, media paths, art, program type assignments, and per-card layout settings |
| `RemoteProfiles.json` | Remote profiles used for button mapping |
| `Hardware.json` | Serial device VID/PID and debug logging flag |
| `card_settings.json` | Saved settings for the card generator |
| `scan_settings.json` | Saved settings for the media scanner |
| `cardplayer_debug.log` | Debug log (only written when `DebugLogging` is enabled) |

---

## Cards.csv Column Reference

Columns are matched by name in the header row — order does not matter and unknown
columns are ignored. Only rows with a value in the `Path` column are imported.

| Column | Description |
|--------|-------------|
| `Type_Digit` | Numeric string matching a player type tab (e.g. `1`, `2`) |
| `Title` | Label shown on the card and overlaid as text (supports multiple lines) |
| `QR_Code` | The string encoded in the QR code (max 32 alphanumeric chars, auto-trimmed) |
| `Path` | Full path to the media file or folder. **Required** — rows without this are skipped on import |
| `Art_Path` | Full path to the front card art image |
| `Art_Fit` | How the front art is scaled: `fill`, `fit`, `square fill`, or `square fit` |
| `Art_Back_Path` | Full path to the back card art image. Always rendered as **fit** (letterboxed). Drawn over the background colour, under the QR code |
| `State` | `new`, `skip`, or `generated` — tracks card generation status |
| `Text_Side` | `front` or `back` — which card face receives the title overlay |
| `Text_Font` | Font family name (blank = system default) |
| `Text_Style` | `Normal`, `Bold`, `Italic`, or `Bold Italic` |
| `Text_Size` | Font size in points |
| `Text_Color` | Hex color for the title overlay (e.g. `#000000`) |
| `Front_Bg_Color` | Hex background color for the front card (e.g. `#FFFFFF`) |
| `Back_Bg_Color` | Hex background color for the back card |


---


## Main Window Bar

### Save/discard buttons
Changes made within the editor are for the most part not saved until you click the Save All button in the top bar, or press "Ctrl+s". 
A blue dot will show next to the discard changes button when there exists an unsaved change, as well as a blue dot next to the top level tab of wherever unsaved changes are. 
Clicking the Discard Changes button at the top will just reload the data from disk, so any changes will be undone since last save.
there are a few exceptions to this, changes to scan settings and print profile settings are saved automatically.

### Remote Passthrough
Once you have a remote setup and a player setup to map the remote, you can enable remote passthrough at the top left of the window bar.
Enabling this will execute any received remote commands as mapped by the player type selected in the drop down even if there is no card currently detected by the hardware.
(If there is a card detected by the hardware, remote passthrough is overridden.)


## Tabs

In order you would set up for the first time

### Hardware
Configure the serial device VID/PID. Changes take effect after restarting the app.

### Players
Defines types of media and assigns one program to launch any media assigned to that type — (photos, music, video game, etc.). Each type has:

- **Program** — the executable to launch with the media path as argument
- **Launch options** — extra CLI arguments (e.g. `--new-window`, `--fullscreen`)
- **Send after launch** — a function to call after the program opens (with optional delay)
- **Dispatch method** — how Remote commands are sent (`sendkeys`, `vk`, or `tcp`)
- **Functions** — named key actions assigned to remote buttons (e.g. Play/Pause → `MEDIA_PLAY_PAUSE`)
- **Remote mapping** — maps each physical remote button to a function (must set up remote profile before you map anything)
- **Shift keys** — defines buttons to activate a secondary remote mapping layer

### Cards
Manages card/media setup. Each row links a QR code string to a media file/folder and a program type.
Scan and auto-populate, enter manually, or load from a CSV. You can also drag a file or folder from
Windows Explorer directly onto a grid cell to fill its path.
You also set the layout and design of the physical cars to be created here; titles, art, colors, font, etc. 
Scan for or set front and back art paths to use images when generating card image files. Titles support
multiple lines (press Enter in the title field).
Below each card preview is the "go fetch" button that will allow you to test what will happen when that card is inserted in the hardware.

### Remote Setup
Defines remote control profiles — the physical buttons and their IR codes. Multiple profiles are supported but only the selected one is active. Changing the remote profile may require adjusting individual mappings in the Players tab.

### Print Generator
Generates card images (fronts and/or backs) from the media library as either individual card PNGs or a grid to print as a sheet. Can generate just the QR as an image for custom setup elsewhere, or a grid of QR codes to print on stickers. Supports custom card size, margins, art fit modes (including square fit/fill), front and back art, title text overlay, and rounded outline cutting guides.



---

## Debug Logging

To enable verbose logging, manually edit `Hardware.json` and set:

```json
{
  "DebugLogging": true
}
```

Logs are written to `cardplayer_debug.log` in the data directory (`%LOCALAPPDATA%\go fetch Card Player`). Disable by setting back to `false`. The app reads this at startup.

---

