# Contributing to ProgramClock

Thanks for your interest in improving ProgramClock! This is a small, single-project
Windows app, so contributing is straightforward.

## Ground rules

ProgramClock is, by design, **privacy-preserving**: your usage data stays on your
machine. Please keep it that way. Contributions must not:

- Add any telemetry, analytics, crash reporting, or ads, or transmit anything about how
  the user uses their computer. The **only** permitted network use is the existing opt-in
  update checker (HTTPS to the public GitHub Releases API, off by default), which sends no
  user data. Don't add other network calls.
- Modify, inject input into, or send window messages to other processes. All
  interaction with other apps must remain **read-only observation**.
- Persist anything outside the local SQLite database
  (`%LOCALAPPDATA%\ProgramClock\programclock.db`) and the optional autostart registry
  key — no writing files elsewhere, no cloud sync. (The updater staging the new build in
  the temp folder is the one exception.)

PRs that cross those lines won't be merged.

## Getting started

### Requirements

- Windows
- [.NET 9](https://dotnet.microsoft.com/download) SDK

### Build & run

```sh
git clone https://github.com/CoreyMills/ProgramClock.git
cd ProgramClock
dotnet build ProgramClock/ProgramClock.csproj
dotnet run --project ProgramClock/ProgramClock.csproj
```

The build must complete with **0 warnings and 0 errors** before you open a PR.

> Note: if the app is already running, the build will fail to overwrite
> `ProgramClock.exe`. Close the running instance (right-click the tray icon → Quit, or
> stop the process) and rebuild.

## Project layout

```
ProgramClock/
  Interop/      Win32 P/Invoke (read-only) + browser address-bar reader (UI Automation)
  Tracking/     Foreground/process probes, the UsageTracker engine, browser/script helpers
  Data/         SQLite Database, repositories (usage, settings, categories, blocklist), models
  Startup/      Autostart (HKCU Run key) toggle
  Update/       Opt-in GitHub-release update checker/downloader (the only network code)
  Tray/         NotifyIcon + context menu
  UI/           MainWindow (dashboard) + DashboardViewModel + theme resources
  App.xaml(.cs) Startup: single-instance, DB init, start tracker, tray icon, hidden start
```

How tracking works: a 1s tick records focused time for the foreground app (and its
active website, for browsers) unless idle; a 5s tick records run time for every visible
app; a 30s tick flushes accumulated deltas to SQLite. Time is counted in fixed per-tick
increments so a sleep/hibernate gap isn't counted.

## Coding conventions

- C# with `Nullable` and `ImplicitUsings` enabled — keep both clean (no nullable
  warnings, no redundant usings).
- Follow the style already in the file you're editing: 4-space indent, `_camelCase`
  private fields, expression-bodied members where they read well.
- All SQLite access goes through the repositories in `Data/` and must be parameterized;
  serialize on the shared connection with `lock (_conn)` as the existing code does.
- Keep UI on the dashboard's MVVM-lite pattern (observable view models, in-place row
  updates that preserve grid selection).
- Avoid editing `.cs`/`.xaml` files with tools that rewrite encoding — these files may
  contain non-ASCII characters that get corrupted.

## Submitting changes

1. Fork and create a topic branch.
2. Make your change; keep commits focused with clear messages explaining the *why*.
3. Build with 0/0, and manually verify the affected behavior (launch the app, exercise
   the feature).
4. Open a PR describing what changed and how you tested it.

## Reporting bugs / requesting features

Open a GitHub issue with:

- What you expected vs. what happened.
- Steps to reproduce.
- Your Windows version and how you're running ProgramClock (built locally vs. release).
