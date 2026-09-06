# GofileUploader

A small, sleek desktop app for uploading files to [gofile.io](https://gofile.io) and grabbing a shareable link — drag & drop, live progress, no ads, no browser tab.

![platform](https://img.shields.io/badge/platform-Windows-blue) ![license](https://img.shields.io/badge/license-AGPL--3.0-lightgrey)

## Features

- **Drag & drop** a file straight onto the window (or click to browse)
- Live upload progress with a smooth, glowing progress bar
- Optional **account token** so uploads land in your gofile account instead of an anonymous one
  - **Log in with Browser**: opens gofile's real login page in your default browser; once you copy your token from your gofile profile page, the app detects it in your clipboard and fills it in automatically
  - If you upload anonymously, the app remembers the guest token gofile hands back so repeat uploads reuse the same guest account instead of creating a new one each time
- Token and folder ID are stored encrypted (Windows DPAPI, tied to your user account) in the registry — never written to a plain file on disk
- Copy or open the resulting download link with one click
- Fluent/Mica design ([WPF-UI](https://github.com/lepoco/wpfui)), dark theme, native to Windows 11

## Download

Grab the latest self-contained `.exe` from the [Releases](../../releases) page — no .NET install required, just run it.

## Platform support

The GUI app is built with WPF, which is Windows-only by design (it runs directly on Windows' own desktop compositor). There is no Linux/macOS build of the GUI.

For Linux/macOS (or just a terminal), use the plain Python CLI in [`cli/`](cli/) instead — it talks to the same gofile API:

```bash
pip install -r cli/requirements.txt
python cli/gofile_upload.py myfile.zip
python cli/gofile_upload.py myfile.zip --token YOUR_TOKEN --folder YOUR_FOLDER_ID
```

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build -c Release
```

To produce a single, self-contained `.exe` (bundles the .NET runtime, no install needed on the target machine):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## How the login flow works

Gofile doesn't offer an OAuth/device-login flow for third-party apps — login is magic-link email only, and the token only ever lives on your account's profile page. Gofile's site is a JS single-page app, so there's no static URL this app can jump straight to for login or your profile; "Log in with Browser" opens gofile's homepage in your own browser, where you log in and find your profile page yourself. The app never touches your credentials or session - it just watches the clipboard for something that looks like a token after you click the button, so once you copy it from gofile's own page, it's filled in for you automatically.

## License

[GNU AGPLv3](LICENSE). If you run a modified version of this software as a network service, you must make the modified source available to users of that service.
