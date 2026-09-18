# iRis Screenshare Tool

Anti-cheat screenshare review and forensics tool for Minecraft. A borderless
full-screen WebView2 app (C# / WPF host + HTML/CSS/JS frontend) with four
forensic modules for reviewing a player's machine during a screenshare.

## Modules

- **Service Checker** — audits forensic Windows services, boot integrity
  (registry vs. tick-count), event-log clears, USN journal state, unexpected
  shutdowns, clock changes and the Recycle Bin.
- **Alt Checker** — scans launcher `accounts.json` files, server logs,
  Discord client storage and browser storage (LevelDB / SQLite readers
  included) to reveal alternate Minecraft and Discord accounts.
- **Prefetch Parser** — parses Windows Prefetch (`.pf`) execution traces
  (MAM/Xpress decompression via ntdll), verifies binary signatures with
  WinVerifyTrust (cheat-cert blacklist, untrusted-store check, catalog
  fallback), matches embedded cheat-trace rules and maps volume paths to
  drive letters.
- **BAM Parser** — reads Background Activity Moderator execution traces from
  the registry, checks binary signatures (including cheat/fake-certificate
  detection), matches cheat-trace rules and detects file replace/rename
  patterns by scanning NTFS USN journals natively.
- **Shared Generics** (`Rules/`) — one rule engine used identically by both
  modules: 26 string/structure rules (clicker combos, protector sections,
  packer entropy, injector imports, .NET signs, imphash) with
  YARA-equivalent semantics, verified rule-by-rule against real YARA.

## Requirements

- Windows 10/11 x64
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)
  — the published build is framework-dependent to stay small
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)

Administrator rights unlock the full scans (event logs, USN journal,
Recycle Bin, Prefetch directory); the app can relaunch itself elevated.

## Build

```powershell
# SDK build
dotnet build ScreenshareTool.sln -c Release

# Minimal framework-dependent single-file publish (~3 MB)
dotnet publish src/ScreenshareTool/ScreenshareTool.csproj `
  -c Release -r win-x64
```

The published app is a single `ScreenshareTool.exe`. The WebView2 native
loader is embedded as a resource and self-extracts to
`%LocalAppData%\iRis-Screenshare-Tool\native\<version>\` on first run, so no
DLL needs to sit next to the executable.

## Project layout

```
src/ScreenshareTool/
  Alt/        alt-account detection (launchers, logs, Discord, browsers)
  Api/        WebView2 bridge (pywebview-compatible shim)
  Assets/     frontend (index.html, style.css, script.js, bg.js, fonts)
  Bam/        BAM registry traces, USN replace detection, scanner
  Forensics/  USN journal, event-log integrity, shared native helpers
  Prefetch/   prefetch parser, signature checks, rule matching, scanner
  Rules/      shared cheat-trace rule engine (Prefetch + BAM Generics)
  Service/    service / boot / drive / recycle-bin checker
```

## Notes

- No namespaces are used in the C# code by convention; all types are global.
- The frontend talks to the host through `window.pywebview.api` over
  WebView2 web messages, restricted to the `https://app.iris.local/` origin.
- Logs are written to `%AppData%\iRis-Screenshare-Tool\logs`.
