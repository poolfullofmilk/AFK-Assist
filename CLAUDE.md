# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Git

Work directly on `master`. Never create a branch or a worktree unless explicitly asked to. If a session starts inside a worktree, move the changes onto `master` in the main checkout before doing anything else.

Commits are a single Title Case sentence. No body, no bullet list, no `Co-Authored-By` trailer, no emoji.

Only `poolfullofmilk <70652416+poolfullofmilk@users.noreply.github.com>` commits and pushes here, absolutely no other account. The `pre-commit` and `pre-push` guards from `~/.gitconfig-poolfullofmilk` enforce it; never bypass them, and stop if one refuses.

## What This Is

A .NET 10 desktop app that keeps a game from marking you idle. It presses keys, clicks and glides the mouse on a jittered schedule while you are away.

Most competitive games kick a player after a few minutes without input. Some only count mouse activity, some only keyboard, so every input is independently selectable.

Since 1.7 it is laid out like `SoTF.Editor`: a WPF window hosts a `BlazorWebView` whose whole UI is MudBlazor, in the shared dark theme. Up to 1.6 it was a single WPF-UI project; do not look for `Views/`, `ViewModels/`, `PresetAppearanceConverter` or CommunityToolkit.Mvvm, they are gone.

The window is `Topmost` on purpose. It is meant to sit over a game while you set it up.

| Project | Holds |
|---|---|
| `AFKAssist.Data` (`net10.0`) | `Helpers` (`AppInfo`, `Formatter`), `Enums` (`LogKind`, `ActionKind`), `ViewModels` (`LogEntry`, `Preset`, `Notice`, `ReleaseInfo`, `SimulationAction`) |
| `AFKAssist.Services` (`net10.0-windows`) | `Simulation/MainViewModel` (all run logic), `Input` (`InputSimulator`, `UserActivity`), `Games` (`GameScanner`, `GameWindowFocus`), `Storage` (`UserSettings`, `RunLog`), `Integration/Updater` |
| `AFKAssist.Desktop` | `App` (single instance, startup, DI), `MainWindow` (WebView2 host, tray, place), `Components` (`Home` and `Shared`), `Theming` (`AppTheme`, `LogKindColors`), `Interop` (`TrayMenu`, `WindowCaptionTheme`, `Explorer`), `wwwroot` |

Packages: MudBlazor and `Microsoft.AspNetCore.Components.WebView.Wpf`, nothing else. No MVVM toolkit, no NLog: `RunLog` covers both run files and failures. `UseWindowsForms` is only for `NotifyIcon`; the csproj removes the `System.Drawing` and `System.Windows.Forms` global usings, and code reaches them through `Drawing` and `Forms` aliases.

## Build, Run, Test

```
dotnet build AFKAssist.slnx
```

```
dotnet run --project AFKAssist.Desktop
```

`AllowUnsafeBlocks` is required in Services and Desktop. The `LibraryImport` source generator emits unsafe marshalling for every P/Invoke, and `UserActivity` takes the address of `[UnmanagedCallersOnly]` methods to hand Windows its hook callbacks.

There is no test project and none is wanted. `GameScanner`'s static constructor runs `Debug.Assert(SelfTestPasses())`, covering name normalization, executable scoring, display names and the reject list. It only fires in Debug. Change any of those and extend the self-test in the same file.

To exercise the scanner without the UI, write a file-based app in the scratchpad with `#:project` on `AFKAssist.Services` and print `GameScanner.InstalledGames`; it is `internal`, so add an `InternalsVisibleTo` there only for that run, or include the source file instead.

A Debug build uses `AFK Assist Debug` for its settings, WebView2 profile and logs (`AppInfo.FolderName`), and its own single instance handle, so it never touches the installed app's files.

## Shipping A Single Executable

```
dotnet publish AFKAssist.Desktop -c Release
```

The result is one self-contained file, `AFKAssist.Desktop\bin\Release\net10.0-windows10.0.17763.0\win-x64\publish\AFKAssist_v<Version>.exe`, about 80 MB. The Release-only `PropertyGroup` holds `AssemblyName`, `RuntimeIdentifier`, `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, `IncludeAllContentForSelfExtract` and `EnableCompressionInSingleFile`, so a plain `dotnet build` stays a fast normal build. `BundleWebRoot` packs `wwwroot` inside the exe and `RemoveLooseWebRoot` deletes the loose copy, so the publish folder holds only the exe. Never use `PublishTrimmed` or Native AOT with WPF.

`<Version>` in `AFKAssist.Desktop.csproj` is the release version with two parts, currently `1.8`, tagged `v1.8`. `AppInfo.Version` reads it from the entry assembly, which pads it to `1.8.0.0`; the `v1.8` tag parses as `1.8`, which sorts below `1.8.0.0` because unset parts count as lower, so a release never flags itself. Bump `<Version>` together with the release tag.

`AddCommitMetadata` stamps the commit the build came from (`SourceRevisionId`, which the SDK reads from git) into `AssemblyMetadata` `Commit`, so the update dialog can list every commit title since the installed build. Publish after committing, or the stamp names the commit before the change. `IncludeSourceRevisionInInformationalVersion` stays false so the product version reads plain.

`app.manifest` makes the window per monitor DPI aware (`PerMonitorV2`), or Windows stretched it as a bitmap on a monitor scaled differently. WinForms warns about that with WFO0003, which `NoWarn` silences: only its `NotifyIcon` is used, never a WinForms window.

## Releasing

The owner asks for each release. The order is: `git pull` (the owner edits the README on GitHub), fresh screenshots, the README, bump `<Version>` and the version paragraph above, commit, push, `dotnet publish AFKAssist.Desktop -c Release`, launch the exe alone from an empty folder, then:

```
gh release create vX.Y "AFKAssist.Desktop/bin/Release/net10.0-windows10.0.17763.0/win-x64/publish/AFKAssist_vX.Y.exe" --target master --title "AFK Assist vX.Y" --notes ""
```

- The asset must keep the name `AFKAssist_vX.Y.exe`: the updater downloads `releases/download/<tag>/AFKAssist_<tag>.exe` and checks it against the `digest` GitHub lists for that name. Up to 1.6 the assets were `AFK.Assist.vX.Y.exe`; those installs only open the release page.
- The body is empty and the exe is the only asset. Nothing in the release, the tag or the commit mentions an AI.
- The owner keeps only the newest release: once the new one is live, `gh release delete vOld --cleanup-tag --yes` and `git fetch --prune --prune-tags`. `releases/latest` keeps working for the update check.
- A Release exe launched from a session reads and writes the owner's real `%AppData%\AFK Assist`. End the test launch with `Stop-Process`, which skips `Closing` and saves nothing.
- The README is sentence case: the name, an `Idle | Running` screenshot table, then `Features`, `Quick start`, `⚠️ Important` and `Technical details` with one to three bullets each.

`Icon.ico` is the `ApplicationIcon`; the tray reads it back with `Icon.ExtractAssociatedIcon(Environment.ProcessPath)`.

## Formatting

Run after every change, before reporting work as done:

```
csharpier format .
```

It formats C#, XAML and the project files. `.razor` files follow the markup rules instead; check them with `powershell -File ~/.claude/scripts/RazorStyle.ps1`.

## Threading Model

Everything in `MainViewModel` runs on the WPF dispatcher. `App` constructs it on the UI thread, Blazor runs its components and event handlers on that same dispatcher, and the tray menu calls in from it too. It starts its loops from those handlers and never calls `ConfigureAwait(false)` itself, so every continuation returns to the dispatcher. That is why there is no `Invoke`, no `CheckAccess` and no locking.

`MainViewModel` is a plain class: setters with side effects (`RunUntilEnabled`, the toggles that log, `SelectedGameKey`, the duration boxes) use the C# 14 `field` keyword, and the page reads properties directly. It raises `Changed` when anything the page shows moves from outside a page event (log lines, run state, the game scan) and `Ticked` once a second while running. `Home` re-renders on `Changed`; only `TimePanel` listens to `Ticked`, so the clock never redraws the whole page.

The deliberate exceptions:

- `LoadAvailableGamesAsync` touches `GameScanner.InstalledGames` inside `Task.Run`, which is what starts the disk scan off the dispatcher. The `Lazy<T>` uses `PublicationOnly`, so a scan that throws is retried on the next access instead of cached as a permanent failure.
- `InputSimulator` awaits with `ConfigureAwait(false)` throughout. One glide is hundreds of steps, and posting each continuation to the dispatcher put that many messages ahead of rendering for a loop that only touches locals and `SendInput`. The seam still holds: `ExecuteSimulationAsync` awaits the action without it, so the log and the counters are back on the UI thread.
- Key and click holds await `Task.Delay` **without** a cancellation token. Cancelling mid-hold would leave the key stuck down inside the game. With Hold Keys Longer a hold lasts up to 3 s, so Stop can take that long to settle; that is still correct.
- `UserActivity`'s hook callbacks run on the UI thread, because that is the thread that installed them (`App` builds the view model there on purpose). Windows skips a hook that stalls past its timeout, so a blocked dispatcher briefly blinds auto pause rather than freezing the mouse.
- `Updater.DownloadAsync` runs on the thread pool and reports progress through `Progress<T>`.

`Start()` fire-and-forgets `RunAsync` with `_ = `, so the Start button returns at once and stays live as Pause. `RunAsync` catches everything itself.

## Run Lifecycle

`Start` → `WaitStartDelayAsync` → `FocusGameAsync` (Switch To Game only) → `RunClockAsync` and `RunScheduleAsync` in parallel → `Finish`.

- **Start delay** restarts the stopwatch when it ends, so the delay is never billed to the duration.
- **Run For** finishes when the stopwatch passes the Hours and Minutes boxes. Pauses stop the stopwatch, so paused time does not count.
- **Run Until** reuses the same two boxes as a clock time. `_stopAt` is captured at `Start` and recaptured whenever the boxes change, never recomputed per tick: the next occurrence of a clock time rolls to tomorrow the instant it passes, and a per-tick lookup would never finish. Clock time keeps running while paused, so this mode can finish during a pause. Flipping the switch converts the boxes, rounding a clock time up and a duration down so flipping back and forth is stable.
- **Pause** unlocks every input through `CanEditConfiguration`. Nothing is captured at `Start` that an edit would miss: the clock reads the boxes each tick, the schedule rebuilds on a speed change and keys are built per simulation. Resume re-validates first.
- **Auto pause** happens when `UserActivity.LastInputTick` moves past `_ignoreInputUntilTick`, a 3 s grace set whenever the clock starts or a resume happens. **Auto resume** happens after 5 s without input, and only for an auto pause; a manual pause stays paused.

The Elapsed and Remaining labels hide leading units that are zero and pad nothing: `5s`, `3m 12s`, `1h 0m 12s` (`Formatter.FormatDuration`). Seconds always show.

`EndsAtLabel` reads `Ends At 18h20` (`Formatter.FormatClock`) beside the run mode switch, in both modes: the captured `_stopAt` in Run Until, the clock plus the remaining time in Run For. While running it raises `Changed` only when its minute moves. `NextActionLabel` counts down to `_nextDueSeconds` in the Run panel, and is empty until the schedule loop sets it; inside a burst whose next slot is already due it reads `Next In 0s`.

A run that finds its game gone, rather than merely unfocused, ends itself with `Stopped Game Closed`. `Finish` then logs one `Sent ... Keys ... Clicks ... Moves In ...` line from `_sentCounts`, which `Start` clears.

## Input Injection

`InputSimulator` calls `SendInput` with a 40-byte `INPUT` struct. Keystrokes fill both `wVk` and `wScan` and do **not** set `KEYEVENTF_SCANCODE`:

- Games reading virtual keys (`WM_KEYDOWN`) see the right letter for the layout.
- Games reading raw input or DirectInput see the right physical scan code.

Setting only one breaks half the games. Do not go back to `keybd_event`.

**Movement keys** are defined by scan code only, fixed on the physical WASD positions (`0x11`, `0x1E`, `0x1F`, `0x20`). `FromScanCode` asks `MapVirtualKeyW(scanCode, MAPVK_VSC_TO_VK)` for the virtual key, so Azerty sends Z and Q, Dvorak sends its own letters, and nothing needs a layout toggle. The labels come from the same scan codes and are read on every render; the window calls `RefreshKeyNames` each time it is activated, which is when a layout switch made elsewhere shows up.

**Custom key** starts from a virtual key. `FromVirtualKey` looks the scan code up with `MapVirtualKeyW` and sets `SimulatedKey.IsExtended` from an explicit range, because `MAPVK_VK_TO_VSC_EX` does not flag arrows, Insert, Delete, Home, End or Page Up/Down; without `KEYEVENTF_EXTENDEDKEY` those land on the number pad.

**Key names** are cached per key, because they are read for every label and every log line; `RefreshKeyNames` calls `ForgetKeyNames` since a layout switch renames them. They come from `GetKeyNameTextW` with the scan code and extended bit, so labels read `W`, `Left`, `1` and `F9` in the active layout instead of `KeyInterop` names like `D1` or `OemTilde`. Names too long for the custom key row (`Caps Lock`, `Page Up`, `Backspace`, ...) are replaced by keycap short names from `s_shortKeyNames`, keyed by virtual key because layout names are localized. The checkbox label also truncates with an ellipsis for anything unlisted.

**Movement** is one to four outward relative slides of 60–1200 px. The direction is a Gaussian around horizontal with a fifth of the slides thrown anywhere, so the angle histogram has no hard edges and no empty vertical band, and the cursor only wanders back to the start about six times in ten, because always returning is a pattern a position series shows up immediately. Each slide follows a cubic curve with a random bend near each end, walked in 3–9 px steps, so a long sweep gets hundreds of them. `Ease` shapes the speed with a random sharpness, every step's delay is jittered around the slide's own duration, Gaussian tremor scaled to the step size lands on all but the last step, and a step in the middle of a sweep occasionally hesitates. `Task.Delay` resolves to about a millisecond on .NET 8 and later, which is what makes the fine steps worth sending. Every slide is clamped by `KeepInside` to the foreground window's rectangle less a margin, so the cursor never wanders onto another monitor or the taskbar; a window too small to hold the margin is left alone. It cancels on Stop; nothing can get stuck.

`RandomInRange` is the one random helper for every `(Minimum, Maximum)` tuple in the app. It maps a log-normal draw from `NextGaussian` onto the range, so values sit low with a long tail to the right, which is the shape human gaps have. A symmetric distribution, flat or triangular, is a signature of its own; nothing natural produces one.

Key presses and clicks share `HoldAsync`, which sends the press, waits and sends the release.

**Numbers.** A slide takes 140–900 ms over 24–320 steps, slides sit 80–900 ms apart, a hesitation (4% per step in the middle half of a sweep) lasts 30–160 ms, tremor is 12% of the step size, and the return lands within a 25 px Gaussian of the start. `KeepInside` keeps 40 px from the window edge and leaves windows under 200 px alone. Key taps hold 70–160 ms, Hold Keys Longer 1–3 s, clicks 60–140 ms, and the actions inside one simulation sit 120–360 ms apart with Randomize on.

**Moves are relative, so Windows accelerates them.** Every move is a relative `MOUSEEVENTF_MOVE` delta, which Windows runs through the pointer speed and Enhance Pointer Precision before it moves the cursor. The pixel ranges above are what is sent, not where the cursor lands, and `KeepInside` clamps the requested delta, so with acceleration on a fast slide can end a little past the 40 px margin. Games reading raw input see the deltas unaccelerated.

**Failures.** `Send` throws `Win32Exception` when `SendInput` reports anything but one event sent. `RunAsync` logs `Blocked By Windows` and stops the run; any other exception logs `Failed With <Type>`. UIPI blocks input from a non-elevated process into an elevated window, and Windows never names UIPI as the cause, so an elevated game needs AFK Assist elevated too.

## Detection

Randomness only hides patterns. What stays traceable no matter what the timing looks like:

- `SendInput` sets `LLKHF_INJECTED` and `LLMHF_INJECTED`; any low-level hook sees them, including this app's own `UserActivity`.
- Raw input from `SendInput` arrives with no device handle.
- Kernel anti-cheat sees injection directly, and can see the `AFK Assist` process and window.
- Nothing in user mode clears any of it. `keybd_event`, scan-code-only input and `PostMessage` are all equal or worse, and `PostMessage` does not even update key state, which is what most games poll.

Behaviour is the half that is worth shaping, because it is also what a server sees without reading a single Windows flag: the per-minute count varies instead of being exact, timing is log-normal rather than symmetric, the mouse does not always return to where it started, and the angles have no hard cutoff.

None of that can be cleared from user mode. Hiding it takes a kernel driver or external USB hardware, which this project does not do: it is anti-cheat evasion and a ban risk of its own. Whether a game acts on the flags is up to the game.

`SendInput` delivers to the foreground window, and a click lands under the cursor, which can move focus. With Switch To Game on, the foreground guard stops that input from reaching anything but the game.

## User Activity

`UserActivity.Watch()` installs `WH_KEYBOARD_LL` and `WH_MOUSE_LL` hooks with a module handle of `0`, which low-level hooks accept, and records `Environment.TickCount64` on every event **without** the injected flag (`LLKHF_INJECTED` 0x10 at offset 8, `LLMHF_INJECTED` 0x01 at offset 12). The app's own `SendInput` always carries that flag, so simulated input never pauses a run. `GetLastInputInfo` cannot be used instead: it counts injected input too.

The hooks are never uninstalled. Windows removes them when the process exits.

## Switch To Game

- **Picker.** A `MudSelect` over `AvailableGames`, a list of process key → display name pairs, with `SelectedGameKey` as its value and `GameNameOf` turning the key back into its name. An empty key is Automatic. Settings store the key, never the display name. A key restored before the scan finishes waits until its item arrives; one that never arrives falls back to Automatic in `LoadAvailableGamesAsync`, which raises `Changed` so the select redraws.
- **Focus.** `GameWindowFocus.TryFocusGameWindow` finds the picked game, or with Automatic the first process with a main window whose key the scanner knows, in whatever order `Process.GetProcesses` returns (two running games pick one arbitrarily), foregrounds it and returns that key. It keeps that `Process`, so `IsForeground` is one `GetWindowThreadProcessId` and `IsRunning` is one `HasExited` instead of a fresh snapshot of every process per simulation. `Finish` calls `Forget`. `ShowWindow(SW_RESTORE)` runs only when `IsIconic` says the game is minimised, and brings it back maximised if it was. On a window that is not minimised `SW_RESTORE` un-maximises it, which 1.5 did to every maximised windowed game on every focus. Foregrounding needs `BringWindowToTop` plus `SetForegroundWindow`, retried inside `AttachThreadInput` when Windows refuses; leave it alone unless tested against a real game.
- **Pre-select.** With Automatic, `FocusGame` pins the found key into `SelectedGameKey` and sets `_isGameAutoSelected`. `Finish` puts Automatic back, `SaveSettings` never persists a pinned game, and any user pick clears the flag through `OnSelectedGameKeyChanged`.
- **Guard.** `ExecuteSimulationAsync` skips the whole simulation and logs `Skipped Game Not Focused` unless `GameWindowFocus.IsForeground` matches the target. With Automatic and no game found there is no target, so nothing is sent.
- With the toggle off, input goes to whatever window has focus.

## Scheduling

One Randomize toggle covers the per-minute count, burst placement, the shuffled slice of actions, holds and gaps. Mouse glides stay random either way. Burst Activity and the two separate Randomize toggles of 1.3 are gone; nobody could tell them apart.

With Randomize on, `CreateMinuteSchedule` draws the count between half and one and a half times the speed setting, never below one, and hands it to `CreateBurstSchedule`. That drops one to three burst starts anywhere in the minute and scatters the actions around them inside `BurstSpreadSeconds`, then sorts, so quiet stretches sit between clusters instead of an even beat. The burst count comes from `RandomInRange((1, 3))`, so a single burst is the most common minute, and every slot is clamped 0.05 s inside the minute. An exact count every minute for hours is the loudest pattern the app can produce, and it is the one a server notices without inspecting anything Windows knows.

With Randomize off, the actions sit on an even beat at the exact speed setting, every simulation runs every enabled action in checkbox order with no gap between them, and holds use the midpoint of their range.

`RunScheduleAsync` polls every 250 ms so Pause and Stop stay responsive, and rebuilds the schedule at each minute boundary **and** whenever the speed changes. A burst packs its slots into 8 s while a glide alone can take seconds, so slots missed during a long simulation fire back to back; collapsing them, as 1.3 did, cut speed 10 down to about three actions a minute. A rebuild mid-minute skips the slots already past, and a minute boundary drops whatever is left.

`MaximumQuietSeconds` caps any silence at 60 s by firing an extra action that uses up no slot. Bursts alone can leave about two minutes quiet across a minute boundary, long enough for some games to kick. `NextDueSeconds` returns the earlier of the next slot and the quiet cap, so `Next In` counts down to whichever fires first.

The first action lands about 800 ms late with Switch To Game on, because focusing waits `FocusSettleDelayMilliseconds` after the stopwatch starts. Do not start the stopwatch later to hide that; the focus time belongs to the run.

Hold and gap ranges live in `(Minimum, Maximum)` tuples on the view model. `NextMilliseconds` picks inclusively when Randomize is on and the midpoint when it is off.

## Game Detection

`GameScanner` is ported from the Adrenalize repository, the reference implementation. Detection changes belong there first; port them back rather than forking.

Sources: Steam (every library in `libraryfolders.vdf`, which lists the primary one too, then each `appmanifest_*.acf`), Epic (`.item` manifests under `C:\ProgramData\Epic`), Riot (every folder beside the client in `RiotClientInstalls.json`), Rockstar (registry plus both Program Files roots), Roblox (the version folder holding `RobloxPlayerBeta.exe`), and `C:\Games`, `D:\Games`, `E:\Games`.

Folders are enumerated behind a `Directory.Exists` guard with `EnumerationOptions`, whose `IgnoreInaccessible` skips folders that deny access. Sources may overlap; `TryAdd` keeps the first folder claiming a key, so no source dedupes on its own.

Steam, Epic, Riot and Rockstar are one method each that fills a list inside a single `try`, so an unreadable file or a missing Steam install ends that source only. Roblox is one query.

Inside each root the main executable is chosen by score: `win64` and `shipping` earn 3, `launcher` costs 5, an exact folder-name match earns 4 and a partial one 2. Names containing `helper`, `handler`, `video`, `service`, `crash`, `report`, `uninstall` or `setup` are vetoed before scoring. The name is computed once per executable and passed down.

Executable names become process keys by lowercasing and stripping `_` and `-`, with aliases for Assetto Corsa and Valorant. `Scan` returns key → display name. The display name is the install folder when it has a space or a capital, title-cased otherwise, with `s_displayNameAliases` for the few that still read badly. The key `launcher` is dropped because it would match anything.

Added here and **still to port back** to Adrenalize:

- Display names, their aliases and the key → display dictionary with `TryAdd`
- Dropping the `launcher` key, and the `handler` and `video` reject tokens
- The `Directory.Exists` enumeration in place of the hand-rolled safe enumerator, and `PublicationOnly` on the cache
- The removed Riot hard-coded roots, source-level dedupe, primary Steam library yield and redundant root guard
- Executable names passed to scoring instead of paths, the inline `launcher` penalty, and regexes without `IgnoreCase`
- `acs` and `acsx86` matched exactly, so `ACShadows` and `ACSyndicate` no longer become Assetto Corsa
- Steam, Epic and Rockstar flattened to one method with one `try`, Roblox as one query, and the inlined score penalty
- The matching self-test changes

The scan runs once per process, well under a second. Games installed while the app runs are not picked up.

## Updates

Taken from `SoTF.Editor`. `Updater.CheckAsync` reads the `releases/latest` redirect without following it, so it needs no token. It runs on every start and from **Help → Check for Updates**; a newer version opens `UpdateDialog` and leaves a red dot on Help and on Check for Updates until restart. What Changed lists every commit title since the installed build through the compare API (`AppInfo.CompareApiUrl`, from the `Commit` stamp); a build without one shows the release body, and no notes hide the list.

Update Now hides the X and Escape until the download fails or the window closes (`SetClosableAsync`), while `MainWindow.IsUpdating` explains a close. `Updater.DownloadAsync` saves `AFKAssist_<tag>.exe` beside the running exe through a `.download` file that must match the asset's SHA-256 `digest` from the release API; a mismatch or a missing digest opens the release page instead, and so does a protected folder or a Debug build (`Updater.CanInstall` wants an exe named `AFKAssist_v*`). Before launching the new exe the dialog stops any run, so no key is left held. The new exe runs `Updater.FinishPreviousVersion` before the single instance check: it waits up to 15 s for the old process and deletes every other `AFKAssist_v*.exe` beside it. Build with `-p:Version=0.9` to see the dialog.

## Run Logs And Settings

`RunLog` writes `Documents\AFK Assist\Logs\Run yyyy-MM-dd HH-mm-ss.txt`. `Open` holds one `StreamWriter` with `AutoFlush` for the whole run, so a crash still leaves every line on disk without reopening the file per line; `Close` ends it. The file keeps milliseconds; the screen shows seconds. `RetentionDays` (30) lives here. `MainViewModel`'s constructor runs `DeleteExpired` through `Task.Run`, which removes `Run *.txt` files whose last write is older than that. Clear Logs asks first, then deletes every run file. Failures that are not part of a run (the page, a background task, the updater, a shell launch) go to `Logs\Errors.txt` through `WriteError`, with the exception type only. Every call swallows its exceptions, and a file held open elsewhere simply stays.

`UserSettings` is a record with `Load` and `Save` on it, because the record is what gets loaded and saved. It holds everything the window shows, plus the window's own spot, and it writes `%AppData%\AFK Assist\settings.json` from the window's `Closing` handler. Nothing saves earlier, so a crash or a `Stop-Process` loses every edit since launch. The record and the release folder name are unchanged since 1.6, so an update keeps every setting. `WindowLeft` and `WindowTop` are plain view model properties; the window fills them in on close (only while it is in its normal state) and reads them back in its constructor, and a spot whose title bar centre no longer lands on a monitor falls back to `CenterScreen`. Missing properties in an older file fall back to their defaults, which is `false` except for `Randomize`, whose parameter default of `true` keeps a 1.3 file from switching it off. `RestoreSettings` clamps every number to the same limits the page uses, sets Run Until before the boxes because the mode converts them, and the constructor clears the log afterwards because restoring is not activity.

## UI Conventions

The look is `SoTF.Editor`'s: `Theming/AppTheme` and the stylesheet's tokens and MudBlazor overrides are copied from it, trimmed to what this page uses. One page, `Components/Home`, is the root component; there is no router, layout or navigation.

- **Layout.** A 55 px glass app bar holds the name and the Help menu (GitHub Project, Check for Updates, About) on the left, and on the right a Running or Paused chip, Stop, and the primary button at the edge (Start, Pause, Resume). Under it three columns, each a `Header` over raised cards (`Elevation="4"`, `rounded-panel pa-4`): Input and Options (360 px), Timing with Speed, Start Delay and Duration (360 px), and Run with `TimePanel` above Activity, which takes the rest and fills to the bottom gutter.
- **Window.** A plain WPF `Window`, 1200 x 760 DIP, `ResizeMode="CanMinimize"`, `Topmost`, background `#202020` like the WebView, with the dark caption from `WindowCaptionTheme`. `CanMinimize` alone removes the resize border and maximize; nothing else is needed now that WPF-UI's chrome is gone. On a work area smaller than that the window shrinks to fit, and `page-fill`'s minimum of 1130 x 580 px makes the page scroll both ways instead of squeezing the columns. Checked at 1186 x 723 (125%), 1186 x 691 and 1186 x 643 at 100%, and 1000 x 560.
- **Options** is one card: Switch To Game with the game `MudSelect` full width under it (`game-select` sets its text to label size), then Randomize and Hold Keys Longer, split by dividers. Each switch carries an `InfoIcon` saying what it does.
- **Run mode** is a `MudSwitch` between `Run For` and `Run Until` at the bottom of the Duration card, with `EndsAtLabel` on the right of the same row.
- **Limits** are `public const int` on `MainViewModel`, read straight from the markup. Only the Hours maximum is a property, because it switches between 8 and 23 with the run mode. `RestoreSettings` clamps against the same constants. Every number field is `NumberField`, whose wrapper greys the spin button at a limit.
- **Presets** are `Preset` records of value, label and an `IsStartDelay` flag, drawn by `PresetRow` as small filled buttons, the active one `Color.Info` (tonal in CSS). `MainViewModel.IsPresetActive` compares against the total its flag names, and `DurationTotalMinutes` is `-1` in Run Until mode so no clock preset lights up. `PresetRow` hands the pick back through `OnPicked`, so the page re-renders and the fields show the new value.
- **UI text** is short Title Case with no ending punctuation; tooltips and dialog prose are short sentences. Units are one letter hugging the number (`15m`, `3m 12s`).
- **Log messages** lead with a past-tense verb: `Pressed W Key`, `Enabled Switch To Game`, `Failed To Focus Game`, `Skipped Game Not Focused`. Errors log the exception type, not its message.
- **The Activity log** is a plain list of `LogEntry` rows in a `panel-flush` paper, coloured through `LogKindColors`. `log-scroll` is a `column-reverse` flex box, so the browser keeps it on the newest row with no script, and leaves it alone once scrolled back. The 150-row cap keeps it small; the run file on disk keeps everything. An empty log shows `No Activity Yet`.
- **Notices** are one `MudAlert` above the log, from `MainViewModel.Notice`. Validation sets an error (`Select At Least One Input`, or `Set A Duration` in Run For mode). Switching Randomize off sets the `Randomize Off Raises Detection Risk` warning, and switching it back on clears exactly that message. `Start` clears any notice.
- **Dialogs** close with their X, except `ConfirmAsync` (Clear Logs), which keeps No and Yes.

Load-bearing pieces:

- **The tray is a WinForms `NotifyIcon`** with the native dark menu from `WiFiWatch` (`TrayMenu`): Start, Pause or Resume, Stop, Open, Exit. Minimising hides the window, a left click or Open shows it again, and Exit closes it so `Closing` still saves. A second launch signals the first through a named `EventWaitHandle` and only shows it.
- **Closing stops the run, then saves.** `Stop` logs the summary and closes the run file; the close button still exits the app with no Keep Running dialog.
- **Custom key capture** is `afkShell.captureKey` in `AppShell.js`: the next `keydown` anywhere in the page resolves with its `keyCode`, which is the Windows virtual key. Bare Shift, Ctrl, Alt and Windows keys are skipped, Escape, a click or the window losing focus cancel with `0`. The custom key checkbox is disabled until a key is picked, which is why `CustomKeyEnabled` alone means a usable key everywhere in the view model.
- **WebView2** keeps its profile in `%AppData%\AFK Assist\WebView2`, has its browser keys and zoom off, and paints `#202020` before the first frame.

## Verifying Changes By Hand

There is no UI test harness. **Ask before launching the app** on this machine: it is `Topmost`, restores and foregrounds games, and injects real input. Check no other copy is running first.

- **Looking at it** — start the Debug exe with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` and drive it over the Chrome DevTools Protocol from a small Node script: `http://127.0.0.1:9333/json` lists the page, `Page.captureScreenshot` captures it, `Runtime.evaluate` clicks. `PrintWindow` captures WebView2 black. `Emulation.setDeviceMetricsOverride` checks smaller windows, and lasts only while its WebSocket stays open.
- **Driving the UI** — checkboxes and switches are `label` elements whose `input` takes a `.click()`; the slider is an `input[type=range]` that needs its value set through the `HTMLInputElement` setter plus `input` and `change` events; buttons are found by their text (`Start`, `15m`, `Clear Logs`), the Help menu by `aria-label`.
- **Keystrokes landing** — count characters in a focused Notepad. Injected holds do not auto-repeat, so a 3 s hold types one character; games poll key state and still see it held.
- **A stand-in game** — copy `powershell.exe` to a known process key such as `overwatch.exe` and run a WinForms form with a `TextBox`. The scanner already knows the key, so focus, pre-select and the foreground guard can be tested without a real game.
- **Focus without the UI** — a file-based app on `AFKAssist.Services` can call `GameWindowFocus.TryFocusGameWindow` with a stand-in's key on a maximised WinForms form. `IsZoomed` must stay true, and after `ShowWindow(SW_MINIMIZE)` a second call must bring it back maximised. Passing the key skips the disk scan.
- **Hooks** — a throwaway console app can install the same hooks, send one injected mouse move and pump messages; the callback must fire and see the injected flag.
- **Auto pause** cannot be triggered from a script, because scripted input is injected and ignored by design. It needs a real hand on the mouse; a test run while someone uses the PC shows `Paused By Input` and `Resumed After Idle` on its own.
- **Screenshots.** `Screenshots/Idle.png` and `Running.png` are CDP captures of a Debug build at 1186 x 723 and scale 1.25. Idle has W Key and Move Mouse checked, Switch To Game and Randomize on, Speed 5, the `5s` and `1h` presets and an empty log. Every toggle change logs a line, so set the state, close with `CloseMainWindow` so it saves, and capture on a second launch. Running has Switch To Game off with no game open, so input lands on AFK Assist itself, Speed 10 and `15m`. Foreground the window and confirm it is foreground, Start, wait about a minute, capture, Stop, and confirm the foreground never changed.
- **Session launches.** A Debug build keeps its settings and WebView2 profile in `AFK Assist Debug` inside the MSIX container, but `Documents` is not redirected, so every test run leaves real files under `Documents\AFK Assist Debug\Logs`. Remove that folder afterwards.

## Things That Look Wrong But Are Not

- The Start button has no disabled state while Stop does: it stays live as Pause and Resume.
- `RunClockAsync` still ticks at 250 ms for auto pause but only refreshes the labels when the second changes, and `EndsAtLabel` only when its minute does. A tick that changes nothing raises nothing.
- `BuildActions` is a plain list of `if`s rather than a collection expression. It reads in the same order as the checkboxes and allocates two objects instead of ten, on a path that runs per simulation.
- `TryValidateConfiguration` asks `BuildActions` whether anything would fire instead of repeating the checkbox list, so validation and the run can never disagree.
- Randomize both shuffles the actions and keeps a random slice of them, so one simulation may press a single key and the next may click and glide. Every enabled input still runs often enough over a minute.
- `DiscoverSteamGameRoots` lets `First(Directory.Exists)` throw when Steam is missing; the source's own `try` turns that into no Steam games.
- `catch` blocks that swallow everything in `GameScanner`, `RunLog` and `UserSettings` are deliberate. A missing path means "not installed", and a log or settings file must never take the app down.
- The WASD checkboxes have no fixed text. Their labels are read from the active keyboard layout, so an Azerty user sees Z and Q. The four labels and `StartDelayPresets` are static; the page reads them through the injected `MainViewModel`, which C# allows because the property shares the type's name.
- `RefreshKeyNames` runs on every window activation and re-renders the page. It is the only moment a layout switch made in another window can show up, and it is cheap.
- The close button exits the app, even mid-run, with no Keep Running dialog; minimising is how it goes to the tray. Closing is also the only thing that saves settings.
- `RunLog` deletes its own run files after 30 days. That predates the rule against retention jobs and stays, because they are throwaway logs rather than user data.
