# ProgramClock

A lightweight Windows tray app that tracks, per application, **how long it has been
running** and **how long you were actually focused in it** — plus a per-website
focused-time breakdown for browsers. Everything stays on your machine.

## Features

- **Run time vs. focused time** — for every user-facing app, see total time the
  process was running and the time it was actually the foreground window with you
  active at the keyboard/mouse.
- **Per-website breakdown** — for recognized browsers (Chrome, Edge, Firefox, Brave,
  Opera, Opera GX, Vivaldi), focused time is split by site (e.g. `youtube.com`) in an
  expandable row. URLs are read locally from the address bar; only the bare host is
  stored.
- **Idle-aware focus** — focused time pauses after a configurable idle threshold of no
  input (default 180s). Run time keeps counting while the app is open.
- **Date ranges** — Today / Week / Month / All.
- **Categories & tags** — apps auto-categorize on first sight (publisher/heuristics) and
  can be reassigned; tag apps as Main / Secondary / Background / Other.
- **Efficiency rating** — a coarse score derived from how your focused time is split
  across tags, with user-editable tag names, weights, and tier labels.
- **Delete / block** — remove an app or website from tracking, or block it so it's never
  tracked again. Configurable Delete/Block hotkeys.
- **System theme** — follows Windows light/dark with a red accent. Configurable refresh
  intervals with live countdown rings and a manual refresh button.
- **Start with Windows** — optional autostart via the current-user registry Run key.

## Privacy

ProgramClock is **entirely local and self-contained**:

- **No network.** There is no HTTP/socket/networking code anywhere in the app.
- **Read-only observation.** It never injects input, sends window messages, writes to,
  or modifies any other process. All OS calls are observational
  (`GetForegroundWindow`, `GetLastInputInfo`, process enumeration, and reading the
  browser address bar via UI Automation).
- **The only thing it persists** is a local SQLite database at
  `%LOCALAPPDATA%\ProgramClock\programclock.db`, plus an optional `HKCU\…\Run` key when
  autostart is enabled.

## Requirements

- Windows
- [.NET 9](https://dotnet.microsoft.com/download) SDK (to build) / runtime (to run)

## Build & Run

```sh
git clone https://github.com/CoreyMills/ProgramClock.git
cd ProgramClock
dotnet build ProgramClock/ProgramClock.csproj
dotnet run --project ProgramClock/ProgramClock.csproj
```

On launch the app runs hidden in the system tray (no window). Right-click the tray icon
to open the dashboard, pause/resume tracking, toggle "Start with Windows", or quit.

## How it works

A 1-second tick records focused time for the foreground app (and its active website, for
browsers) unless you're idle; a 5-second tick records run time for every visible app; a
30-second tick flushes the accumulated deltas to SQLite. Time is counted in fixed
per-tick increments, so a sleep/hibernate gap is simply not counted.

## Tech

.NET 9 · WPF + WinForms (tray) · SQLite (`Microsoft.Data.Sqlite`) · WMI
(`System.Management`, local, for console/script attribution) · single C# project.
