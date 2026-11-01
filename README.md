# Nocturne

A native Windows live wallpaper engine built with C# / .NET 10 WPF.  
Play videos, GIFs, and images as your desktop wallpaper — behind your icons.

## Features

- **Live wallpapers**: MP4, WebM, MKV, AVI, MOV, GIF, APNG, PNG, JPG, BMP, WebP, and more
- **Multi-monitor support**: different wallpaper per display
- **Auto-pause**: pauses on fullscreen apps/games and resumes automatically
- **Library management**: import, remove, rename, favorites, collections
- **Playback settings**: speed, volume, scaling (Fill/Fit/Stretch)
- **Tray icon**: minimize to tray, pause/resume, exit
- **Closing the app restores your original desktop wallpaper**

## Download

Download the latest release from the [Releases](https://github.com/ItsJamessYT/Nocturne/releases) page.  
Extract the zip and run `Nocturne.exe`.

## Build from source

Requirements:
- .NET 10 SDK
- Windows 10/11 (x64)

```powershell
git clone https://github.com/ItsJamessYT/Nocturne.git
cd Nocturne
dotnet build Nocturne.sln -c Debug
```

Run:
```powershell
.\src\Nocturne\bin\x64\Debug\net10.0-windows\Nocturne.exe
```

## Publish a release

```powershell
dotnet publish src\Nocturne\Nocturne.csproj -c Release -r win-x64 --self-contained true
```

Zip the `publish` folder and attach to a GitHub Release.

## Architecture

- **WPF + WinForms** (tray icon only)
- **LibVLCSharp** for video/GIF playback into native child HWNDs
- **WorkerW reparenting** to render behind desktop icons
- **IShellItemImageFactory** for video thumbnails
- Data stored in `%AppData%\Nocturne`

## License

MIT
