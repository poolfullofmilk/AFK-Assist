# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Git

Work directly on `master`. Never create a branch or a worktree unless explicitly asked to. If a session starts inside a worktree, move the changes onto `master` in the main checkout before doing anything else.

Commits are a single Title Case sentence. No body, no bullet list, no `Co-Authored-By` trailer, no emoji.

## What This Is

A single-project .NET 10 WPF desktop app that keeps a game from marking you idle. It presses keys, clicks and glides the mouse on a jittered schedule while you are away.

Most competitive games kick a player after a few minutes without input. Some only count mouse activity, some only keyboard, so every input is independently selectable.

The UI is Fluent through WPF-UI. Version 4 was Windows Forms; do not look for `Form.cs`, `Interop.cs` or `RandomDelay.cs`, they are gone.

The window is `Topmost` on purpose. It is meant to sit over a game while you set it up.

## Build, Run, Test

```
dotnet build
```

`AFK Assist.slnx` only points at the one `.csproj`. Build either.

`AllowUnsafeBlocks` is required twice over. The `LibraryImport` source generator emits unsafe marshalling for every P/Invoke, and `UserActivity` takes the address of `[UnmanagedCallersOnly]` methods to hand Windows its hook callbacks.

There is no test project and none is wanted. `GameScanner`'s static constructor runs `Debug.Assert(SelfTestPasses())`, covering name normalization, executable scoring, display names and the reject list. It only fires in Debug. Change any of those and extend the self-test in the same file.

To exercise the scanner without the UI, compile `Services/GameScanner.cs` into a throwaway console project and print `GameScanner.InstalledGames`. It is `internal`, so include the source file rather than referencing the exe.

## Shipping A Single Executable

```
dotnet publish -c Release
```

The result is one self-contained file at `bin\Release\net10.0-windows\win-x64\publish\AFK Assist.exe`. Five `.csproj` properties produce it, all five required, and they sit in a Release-only `PropertyGroup` so a plain `dotnet build` copies the app instead of the whole runtime:

| Property | Effect if removed |
| --- | --- |
| `SelfContained` | Target machine must have .NET 10 installed |
| `RuntimeIdentifier` | Cannot self-contain without a concrete target; `win-x64` here |
| `PublishSingleFile` | Publish folder fills with loose runtime DLLs |
| `IncludeNativeLibrariesForSelfExtract` | WPF's native libraries land beside the exe |
| `EnableCompressionInSingleFile` | Exe roughly doubles in size |

Do not reach for `PublishTrimmed` or Native AOT. WPF is not trim-safe and is unsupported under AOT.

`<AssemblyName>`, `<RootNamespace>`, `<Product>` and `<Company>` are not in the project because the SDK defaults already produce `AFK Assist` and `AFK_Assist`.

`<Version>` is the release version with two parts, currently `1.3`, tagged `v1.3`. `UpdateChecker.CurrentVersion` reads it back through `Assembly.GetName().Version`, which pads it to `1.3.0.0`; the `v1.3` tag parses as `1.3`, which sorts below `1.3.0.0` because unset parts count as lower, so a release never flags itself. Dialogs print two parts. A tag without a minor part, like the old `v1`, does not parse at all. Bump `<Version>` together with the release tag.

`DebugType` is `embedded`, so symbols ride inside the single file and Debug builds keep them. `SatelliteResourceLanguages` is `en`, which drops 8 MB of translated WPF exception strings from an app whose own text is English only.

Debug builds land at `bin\Debug\net10.0-windows\AFK Assist.exe`, with no runtime identifier folder.

`Icon.ico` is both the `ApplicationIcon` and a WPF `Resource` shown in the title bar. Its first frame is 32 px because `ui:ImageIcon` decodes the first frame only.

## Formatting

Run after every change, before reporting work as done:

```
csharpier format .
```

It is a global tool, invoked as `csharpier`, not `dotnet csharpier`. It formats C# and rewrites the `.csproj`; do not hand-indent project files. It does not touch XAML, which follows the rules in [UI Conventions](#ui-conventions).

## Project Layout

```
AFK Assist.csproj / .slnx
App.xaml                              Application entry, WPF-UI dictionaries
Views/MainWindow.xaml(.cs)            The only window
Views/PresetAppearanceConverter.cs    Highlights the active preset chip
ViewModels/MainViewModel.cs           All application logic
Services/InputSimulator.cs            SendInput keys, clicks and mouse movement
Services/UserActivity.cs              Low-level hooks that notice real hands
Services/GameWindowFocus.cs           Foregrounds a running game, checks the foreground
Services/GameScanner.cs               Finds installed games on disk
Services/UpdateChecker.cs             GitHub latest-release comparison
Services/UserSettings.cs              Settings persisted to %AppData%
Services/RunLog.cs                    One text file per run under Documents
Icon.ico, Screenshot-*.png            App icon, README screenshots
```

No dependency injection, navigation, messenger or repository layer. One window, one view model, seven services. Keep it that way.

## Threading Model

Everything in `MainViewModel` runs on the WPF dispatcher. It starts its loops from UI-thread command handlers and never calls `ConfigureAwait(false)` itself, so every continuation returns to the dispatcher. That is why there is no `Invoke`, no `CheckAccess` and no locking.

The deliberate exceptions:

- `LoadAvailableGamesAsync` touches `GameScanner.InstalledGames` inside `Task.Run`, which is what starts the disk scan off the dispatcher. The `Lazy<T>` uses `PublicationOnly`, so a scan that throws is retried on the next access instead of cached as a permanent failure.
- `InputSimulator` awaits with `ConfigureAwait(false)` throughout. One glide is hundreds of steps, and posting each continuation to the dispatcher put that many messages ahead of rendering for a loop that only touches locals and `SendInput`. The seam still holds: `ExecuteSimulationAsync` awaits the action without it, so the log and the counters are back on the UI thread.
- Key and click holds await `Task.Delay` **without** a cancellation token. Cancelling mid-hold would leave the key stuck down inside the game. With Hold Keys Longer a hold lasts up to 3 s, so Stop can take that long to settle; that is still correct.
- `UserActivity`'s hook callbacks run on the UI thread, because that is the thread that installed them. Windows skips a hook that stalls past its timeout, so a blocked dispatcher briefly blinds auto pause rather than freezing the mouse.

`Start()` fire-and-forgets `RunAsync` with `_ = `. An async `[RelayCommand]` blocks re-entry while it runs, which would kill the Pause button for the whole run. `RunAsync` catches everything itself.

## Run Lifecycle

`Start` → `WaitStartDelayAsync` → `FocusGameAsync` (Switch To Game only) → `RunClockAsync` and `RunScheduleAsync` in parallel → `Finish`.

- **Start delay** restarts the stopwatch when it ends, so the delay is never billed to the duration.
- **Run For** finishes when the stopwatch passes the Hours and Minutes boxes. Pauses stop the stopwatch, so paused time does not count.
- **Run Until** reuses the same two boxes as a clock time. `_stopAt` is captured at `Start` and recaptured whenever the boxes change, never recomputed per tick: the next occurrence of a clock time rolls to tomorrow the instant it passes, and a per-tick lookup would never finish. Clock time keeps running while paused, so this mode can finish during a pause. Flipping the switch converts the boxes, rounding a clock time up and a duration down so flipping back and forth is stable.
- **Pause** unlocks every card through `CanEditConfiguration`. Nothing is captured at `Start` that an edit would miss: the clock reads the boxes each tick, the schedule rebuilds on a speed change and keys are built per simulation. Resume re-validates first.
- **Auto pause** happens when `UserActivity.LastInputTick` moves past `_ignoreInputUntilTick`, a 3 s grace set whenever the clock starts or a resume happens. **Auto resume** happens after 5 s without input, and only for an auto pause; a manual pause stays paused.

The Elapsed and Remaining labels hide leading units that are zero and pad nothing: `5s`, `3m 12s`, `1h 0m 12s`. Seconds always show.

`EndsAtLabel` reads `Ends At 18h20` under the run mode switch, in both modes: the captured `_stopAt` in Run Until, the clock plus the remaining time in Run For. It refreshes every tick while a run is on, and on an edit while it is off. `NextActionLabel` counts down to `_nextDueSeconds` beside the Activity header, and is empty until the schedule loop sets it.

A run that finds its game gone, rather than merely unfocused, ends itself with `Stopped Game Closed`. `Finish` then logs one `Sent ... Keys ... Clicks ... Moves In ...` line from `_sentCounts`, which `Start` clears.

## Input Injection

`InputSimulator` calls `SendInput` with a 40-byte `INPUT` struct. Keystrokes fill both `wVk` and `wScan` and do **not** set `KEYEVENTF_SCANCODE`:

- Games reading virtual keys (`WM_KEYDOWN`) see the right letter for the layout.
- Games reading raw input or DirectInput see the right physical scan code.

Setting only one breaks half the games. Do not go back to `keybd_event`.

**Movement keys** are defined by scan code only, fixed on the physical WASD positions (`0x11`, `0x1E`, `0x1F`, `0x20`). `FromScanCode` asks `MapVirtualKeyW(scanCode, MAPVK_VSC_TO_VK)` for the virtual key, so Azerty sends Z and Q, Dvorak sends its own letters, and nothing needs a layout toggle. The labels come from the same scan codes, and `InputLanguageChanged` refreshes every binding when the layout switches, though that event only fires while the app has focus.

**Custom key** starts from a virtual key. `FromVirtualKey` looks the scan code up with `MapVirtualKeyW` and sets `SimulatedKey.IsExtended` from an explicit range, because `MAPVK_VK_TO_VSC_EX` does not flag arrows, Insert, Delete, Home, End or Page Up/Down; without `KEYEVENTF_EXTENDEDKEY` those land on the number pad.

**Key names** are cached per key, because they are read for every label and every log line; the `InputLanguageChanged` handler calls `ForgetKeyNames` since a layout switch renames them. They come from `GetKeyNameTextW` with the scan code and extended bit, so labels read `W`, `Left`, `1` and `F9` in the active layout instead of `KeyInterop` names like `D1` or `OemTilde`. Names too long for the custom key row (`Caps Lock`, `Page Up`, `Backspace`, ...) are replaced by keycap short names from `s_shortKeyNames`, keyed by virtual key because layout names are localized. The checkbox text also trims with an ellipsis for anything unlisted.

**Movement** is one to four outward relative slides of 60–1200 px. The direction is a Gaussian around horizontal with a fifth of the slides thrown anywhere, so the angle histogram has no hard edges and no empty vertical band, and the cursor only wanders back to the start about six times in ten, because always returning is a pattern a position series shows up immediately. Each slide follows a cubic curve with a random bend near each end, walked in 3–9 px steps, so a long sweep gets hundreds of them. `Ease` shapes the speed with a random sharpness, every step's delay is jittered around the slide's own duration, Gaussian tremor scaled to the step size lands on all but the last step, and a step in the middle of a sweep occasionally hesitates. `Task.Delay` resolves to about a millisecond on .NET 8 and later, which is what makes the fine steps worth sending. Every slide is clamped by `KeepInside` to the foreground window's rectangle less a margin, so the cursor never wanders onto another monitor or the taskbar; a window too small to hold the margin is left alone. It cancels on Stop; nothing can get stuck.

`RandomInRange` is the one random helper for every `(Minimum, Maximum)` tuple in the app. It maps a log-normal draw from `NextGaussian` onto the range, so values sit low with a long tail to the right, which is the shape human gaps have. A symmetric distribution, flat or triangular, is a signature of its own; nothing natural produces one.

Key presses and clicks share `HoldAsync`, which sends the press, waits and sends the release.

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

- **Picker.** The combo box binds `AvailableGames`, a list of process key → display name pairs, with `DisplayMemberPath="Value"`, `SelectedValuePath="Key"` and `SelectedValue` on `SelectedGameKey`. An empty key is Automatic. Settings store the key, never the display name. A key restored before the scan finishes waits inside the combo box until its item arrives; one that never arrives falls back to Automatic in `LoadAvailableGamesAsync`.
- **Focus.** `GameWindowFocus.TryFocusGameWindow` finds the picked game, or with Automatic the first running process whose key the scanner knows, foregrounds it and returns that key. It keeps that `Process`, so `IsForeground` is one `GetWindowThreadProcessId` and `IsRunning` is one `HasExited` instead of a fresh snapshot of every process per simulation. `Finish` calls `Forget`. A minimised game is restored with `SW_RESTORE`, never forced to maximise. Foregrounding needs `BringWindowToTop` plus `SetForegroundWindow`, retried inside `AttachThreadInput` when Windows refuses; leave it alone unless tested against a real game.
- **Pre-select.** With Automatic, `FocusGame` pins the found key into `SelectedGameKey` and sets `_isGameAutoSelected`. `Finish` puts Automatic back, `SaveSettings` never persists a pinned game, and any user pick clears the flag through `OnSelectedGameKeyChanged`.
- **Guard.** `ExecuteSimulationAsync` skips the whole simulation and logs `Skipped Game Not Focused` unless `GameWindowFocus.IsForeground` matches the target. With Automatic and no game found there is no target, so nothing is sent.
- With the toggle off, input goes to whatever window has focus.

## Scheduling

With Burst Activity on, `CreateBurstSchedule` drops one to three burst starts anywhere in the minute and scatters the actions around them inside `BurstSpreadSeconds`, then sorts, so quiet stretches sit between clusters instead of an even beat. The count per minute is still exact.

`CreateMinuteSchedule` on the view model gives each action a slot and places it at the slot start plus up to ±35% of a slot width, clamped inside the minute. The count per minute is exact. Slots never overlap, because the latest point of one slot sits 30% of a slot before the earliest point of the next, so the array needs no sort. With Randomize Intervals off the jitter is zero.

With Randomize Intervals on, the count itself is drawn between half and one and a half times the speed setting, never below one. An exact count every minute for hours is the loudest pattern the app can produce, and it is the one a server notices without inspecting anything Windows knows.

`RunScheduleAsync` polls every 250 ms so Pause and Stop stay responsive, and rebuilds the schedule at each minute boundary **and** whenever the speed changes. Slots missed while a long simulation ran, or while a rebuild landed mid-minute, collapse into one action instead of firing back to back.

The first action lands about 800 ms late with Switch To Game on, because focusing waits `FocusSettleDelayMilliseconds` after the stopwatch starts. Do not start the stopwatch later to hide that; the focus time belongs to the run.

Hold and gap ranges live in `(Minimum, Maximum)` tuples on the view model. `NextMilliseconds` picks inclusively when Randomize Intervals is on and the midpoint when it is off.

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

## Update Checking

`UpdateChecker.CheckAsync` sends one non-redirecting `GET` to the GitHub `releases/latest` URL, reads the tag from the `Location` header and returns `(Latest, ReleaseUrl)` only when that tag is newer, `null` otherwise. No token, no JSON, no User-Agent; github.com answers the redirect without one. Every exception returns `null`; a failed check must never interrupt a run. The startup check is silent when up to date; the title bar button reports either way.

## Run Logs And Settings

`RunLog` writes `Documents\AFK Assist\Logs\Run yyyy-MM-dd HH-mm-ss.txt`. `Open` holds one `StreamWriter` with `AutoFlush` for the whole run, so a crash still leaves every line on disk without reopening the file per line; `Close` ends it. The file keeps milliseconds; the screen shows seconds. `RetentionDays` lives here, the constructor calls `DeleteExpired` off the dispatcher, Clear Log Files deletes all of them, and every call swallows its exceptions.

`UserSettings` is a record with `Load` and `Save` on it, because the record is what gets loaded and saved. It holds everything the window shows, plus the window's own spot, and it writes `%AppData%\AFK Assist\settings.json` from the window's `Closing` handler. `WindowLeft` and `WindowTop` are plain view model properties rather than bound ones; the window fills them in on close and reads them back in its constructor, and a spot whose title bar centre no longer lands on a monitor falls back to `CenterScreen`. Missing properties in an older file fall back to their defaults. `RestoreSettings` clamps every number to the same limits the window uses, sets Run Until before the boxes because the mode converts them, and the constructor clears the log afterwards because restoring is not activity.

## UI Conventions

The window is `ui:FluentWindow` with `ExtendsContentIntoTitleBar` and Mica. `SystemThemeWatcher.Watch(this)` applies the Windows theme and accent on the first call and follows both live; Mica and accent updates are its defaults. `WindowBackdropType="Mica"` stays in XAML because the watcher only reapplies the backdrop on a theme change. `Background` and `Foreground` come from the FluentWindow style. The accent drives checked boxes, toggles, the primary button, the slider, the progress bar and the active preset chip; on a machine with a grey Windows accent they are all grey, which is correct.

Control ladder, first rung that works wins: a WPF-UI control (`ui:Card`, `ui:Button`, `ui:TextBlock`, `ui:ToggleSwitch`, `ui:NumberBox`, `ui:InfoBar`, `ui:ImageIcon`), then a plain WPF control WPF-UI restyles (`CheckBox`, `Slider`, `ComboBox`, `ProgressBar`, `ScrollViewer`, `ItemsControl`), then a `Style` or inline property. The root `Grid.Resources` holds four entries and no more: the keyed `ChipButton` style and an implicit `ui:NumberBox` style, both `BasedOn` the WPF-UI defaults, plus the `ChipRow` panel and `PresetChip` template that both preset lists share. The one converter is reached through `{x:Static}`, not a resource.

XAML: `<!-- Name -->` comments above a group, naming its main element only. One attribute per line, aligned under the first.

- **Layout.** Mouse and Keyboard checkboxes sit in the Input card, Move Mouse among the mouse ones, and every on/off behaviour is a toggle in the Options card. The Keyboard column's five rows set the shared row's height, and the Options card stretches to it.
- **Run mode** is a `ui:ToggleSwitch` between two labels, `Run For` and `Run Until`, at the bottom of the Duration card, with `EndsAtLabel` right-aligned in the same row.
- **Limits** are `public const double` on `MainViewModel`, bound with `{x:Static}`. Only the Hours maximum is a binding, because it switches between 8 and 23 with the run mode. `RestoreSettings` clamps against the same constants.
- **Presets** are `Preset` records of value, label and an `IsStartDelay` flag, so both lists share one `PresetChip` template and one `ApplyPresetCommand`. `StartDelayPresets` is fixed; `DurationPresets` swaps with the run mode, from durations to clock jumps where `0` means the next midnight. A chip's `Appearance` is a `MultiBinding` of the preset and both totals, and `PresetAppearanceConverter` picks the one the flag names. `DurationTotalMinutes` is `-1` in Run Until mode so no chip lights up there.
- **Tooltips** only on the icon-only update button, where it is the label. Every unlabelled input carries `AutomationProperties.Name`.
- **UI text** is short Title Case with no ending punctuation. Units are one letter hugging the number (`15m`, `3m 12s`).
- **Log messages** lead with a past-tense verb: `Pressed W Key`, `Enabled Switch To Game`, `Failed To Focus Game`, `Skipped Game Not Focused`. Errors log the exception type, not its message, which is not Title Case and often ends in a full stop.
- **The Activity log** is an `ItemsControl` inside a `ScrollViewer`, bound to `ObservableCollection<LogEntry>`. There is no selection or hover chrome to strip, and the 150-row cap keeps the un-virtualized list small enough that it never needs a templated `ScrollViewer`; the run file on disk keeps everything. Each entry's `LogKind` colours its message through `DynamicResource` theme brushes, and the timestamp is its own `Auto` column. An empty log shows `No Activity Yet` through a `DataTrigger` on `LogEntries.Count`.

Load-bearing XAML:

- **Number boxes bind with `UpdateSourceTrigger=PropertyChanged`.** `ui:NumberBox.Value` defaults to `LostFocus`, and neither its spin buttons nor a click on `ui:Button` move focus out of the box, so the value never reached the view model. The backing properties are `double?` because an emptied box reports `null`; every read coalesces with `?? 0`. The implicit style sets `MaxDecimalPlaces="0"`, since the default of 6 accepts `1.5` hours.
- **The Input and Options cards share one `Auto` row**, both `VerticalAlignment` and `VerticalContentAlignment` set to `Stretch`. That makes them equal height with headers on one line. `ui:Card` defaults its content to `Center`; `Top` does not fix that, `Stretch` does.
- **The custom key checkbox is disabled until a key is picked**, which is why `CustomKeyEnabled` alone means a usable key everywhere in the view model.

Load-bearing code-behind:

- **The tray icon comes from the `WPF-UI.Tray` package**, reached through the `tray` clr-namespace because it has no xmlns of its own. Minimising hides the window, a left click or Open shows it again, and Exit closes it so `Closing` still saves. Its `ContextMenu` lives outside the visual tree and inherits no `DataContext`, so the code-behind sets one.
- **Log autoscroll is posted.** `ScrollToEnd` straight from `CollectionChanged` runs before the new row is measured and stops one row short. Keep the `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)`.
- **The view model is constructed in code-behind.** `d:DataContext` is design-time only.
- **The window cannot be resized or maximized.** `ResizeMode="CanMinimize"` drops the resize frame and maximize style, and the title bar needs `CanMaximize="False"` and `ShowMaximize="False"` as well, because WPF-UI draws its own buttons and handles double clicks itself.
- **The window has no `Height` in XAML.** It opens with `SizeToContent="Height"`, and `LockHeightToContent` switches to manual once laid out, capped at the work area so a small screen scrolls the configuration instead. The lock is posted at `Background` priority and re-applies `SizeToContent` first, because `ui:InfoBar` is still measured open on the first pass. The log sits in a `*` row, so an empty log asks for no height.
- **`MakeRoomForNotice` sets `Height` absolutely** to the base captured at lock time plus the bar, and listens to both `SizeChanged` and `IsVisibleChanged`. `SizeChanged` does not fire when the bar collapses, so without the second event the window never shrinks back. The earlier version only followed along while `Height` still matched the old minimum, and left a gap above the Time card whenever it did not.
- **Custom key capture** runs on the button's `PreviewKeyDown` only while `IsCapturingCustomKey` is set. `Key.System` is unwrapped for Alt combinations, bare modifiers (the consecutive Shift, Ctrl and Alt range plus both Windows keys) are ignored, Escape cancels, and `LostKeyboardFocus` cancels too.

The notice `ui:InfoBar` is invisible to UI Automation. Check it with a screenshot.

## Verifying Changes By Hand

There is no UI test harness. **Ask before launching the app** on this machine: it is `Topmost`, restores and foregrounds games, and injects real input.

- **Screenshots** — `PrintWindow` with flag `2` against the window handle, after `SetProcessDPIAware` so the capture is not cropped.
- **Driving the UI** — `System.Windows.Automation` from Windows PowerShell. Buttons expose `InvokePattern`, checkboxes and toggles `TogglePattern`, the slider `RangeValuePattern`, number boxes `ValuePattern`, and the game picker `ExpandCollapsePattern` with `SelectionItemPattern` on its items.
- **Keystrokes landing** — count characters in a focused Notepad. Injected holds do not auto-repeat, so a 3 s hold types one character; games poll key state and still see it held.
- **A stand-in game** — copy `powershell.exe` to a known process key such as `overwatch.exe` and run a WinForms form with a `TextBox`. The scanner already knows the key, so focus, pre-select and the foreground guard can be tested without a real game.
- **Hooks** — a throwaway console app can install the same hooks, send one injected mouse move and pump messages; the callback must fire and see the injected flag.
- **Auto pause** cannot be triggered from a script, because scripted input is injected and ignored by design. It needs a real hand on the mouse.

## Comment Style

Comments are very short and clear, a couple of words, at most eight to ten when genuinely needed. Never multi-line. Every word starts with a capital letter. No ending punctuation. Always on their own line above the code they describe.

In C#, comments are allowed only inside method bodies or above a group of related fields or properties. No XML documentation comments. No comments on view models, models, services, interfaces or class declarations. No comments in `.csproj` files. Never comment obvious code, and never write a comment that explains something to the reader of this file rather than to the reader of the code.

In markup, comment only above a group or chunk of elements, and the comment holds the name of that group's main element and nothing else. XAML uses `<!-- Name -->`, Razor uses `@* Name *@`. Never comment above text, parameters, bindings, individual attributes or small markup fragments, and never write a descriptive markup comment.

```
@* Records Table *@
<MudTable>
...
</MudTable>
```

## Code Style

Full descriptive names for variables, fields, properties and methods. No abbreviations, no single letters: `settings`, not `s`; `configuration`, not `cfg`; `cancellationToken`, not `ct`. This covers regex capture groups, tuple elements and lambda parameters too.

`var` for locals, as `.editorconfig` asks. Write the type only when the right-hand side does not carry it: a target-typed `new()`, a collection expression, a declaration without an initializer, or a bare `null`.

```csharp
var executableName = Path.GetFileNameWithoutExtension(executablePath);
HashSet<string> processNames = new(StringComparer.OrdinalIgnoreCase);
List<string> roots = [];
string? bestExecutablePath = null;
```

`#region` blocks wrap methods only, never fields or properties, and only when there would be two or more. No blank line directly after `#region` or directly before `#endregion`; one blank line before `#region` and one after `#endregion`.

```
#region
<SomeMethod>
#endregion
```

UI strings follow the comment rules: short, every word capitalized, no ending punctuation. That covers labels, titles, helper text, validation messages and localization text. "Select At Least One Input", not "Please select at least one input."

Control choice runs down a ladder, first rung that works wins. In Blazor: MudBlazor, then Bootstrap classes, then a MudBlazor component's `Style`, then custom CSS; `MudElement` beats plain HTML, and plain HTML is last. The WPF equivalent is in [UI Conventions](#ui-conventions).

Razor components keep their logic in the code-behind. Never open an `@code` block in a `.razor` file when a `.razor.cs` exists beside it.

## Before Making Changes

Read every file the change touches first: code-behind, services, models, interfaces, registrations and the project file. Do not assume the architecture.

## Things That Look Wrong But Are Not

- `Stop` has `CanExecute = nameof(IsRunning)` while Start/Pause has none, so the primary button stays live as Pause and Resume.
- `RunClockAsync` still ticks at 250 ms for auto pause but only refreshes the labels when the second changes, and `EndsAtLabel` only when its minute does. A tick that changes nothing raises nothing.
- `BuildActions` is a plain list of `if`s rather than a collection expression. It reads in the same order as the checkboxes and allocates two objects instead of ten, on a path that runs per simulation.
- `TryValidateConfiguration` asks `BuildActions` whether anything would fire instead of repeating the checkbox list, so validation and the run can never disagree.
- Randomize Simulation both shuffles the actions and keeps a random slice of them, so one simulation may press a single key and the next may click and glide. Every enabled input still runs often enough over a minute.
- `CheckForUpdatesAsync` takes a `bool?`. The title bar button passes nothing and the constructor passes `true`, because a `RelayCommand` cannot turn a XAML string into a `bool`.
- `DiscoverSteamGameRoots` lets `First(Directory.Exists)` throw when Steam is missing; the source's own `try` turns that into no Steam games.
- `OnRunUntilEnabledChanged` raises `HoursMaximum` by hand before moving the boxes. The generated notification fires only after the partial method, and the Hours box would otherwise clamp a clock hour of 17 to the old maximum of 8.
- `catch` blocks that swallow everything in `GameScanner`, `RunLog` and `UserSettings` are deliberate. A missing path means "not installed", and a log or settings file must never take the app down.
- The WASD checkboxes have no fixed text. Their labels are read from the active keyboard layout, so an Azerty user sees Z and Q.
