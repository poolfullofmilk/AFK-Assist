# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Git

Work directly on `master`. Never create a branch or a worktree unless explicitly asked to. If a session starts inside a worktree, move the changes onto `master` in the main checkout before doing anything else.

Commits are a single Title Case sentence. No body, no bullet list, no `Co-Authored-By` trailer, no emoji.

## What This Is

A single-project .NET 10 Windows desktop application that keeps a game from marking you idle. It presses movement keys and clicks the mouse on a jittered schedule while you are away from the machine.

The problem it solves: most competitive games kick a player after a few minutes without input. Some of them only count mouse activity, some only count keyboard, so both are available and independently selectable.

It is a WPF app using the Fluent design language through WPF-UI. Version 4 was Windows Forms; version 5 is a full rewrite. The behaviour is the same, the code is not — do not look for `Form.cs`, `Interop.cs`, or `RandomDelay.cs`, they are gone.

The window is `Topmost` on purpose. It is meant to sit over a game while you set it up.

## Build, Run, Test

```
dotnet build
```

`AFK Assist.slnx` exists for Visual Studio and holds nothing but a pointer to the one `.csproj`. Build either; nothing in the build depends on the solution file.

`AllowUnsafeBlocks` is required even though no source file contains the `unsafe` keyword. The `LibraryImport` source generator emits unsafe marshalling code for every P/Invoke in `InputSimulator.cs` and `GameWindowFocus.cs`, and removing the property fails the build with `SYSLIB1062`.

There is no test project and none is wanted. Two `Debug.Assert` checks stand in for one:

- `SimulationSchedule.CreateForOneMinute` asserts the schedule holds exactly the requested number of actions and that all of them land inside the minute.
- `GameScanner`'s static constructor asserts `SelfTestPasses()`, which covers process-name normalization, executable scoring, and the reject list.

Both only fire in Debug. If you change schedule maths, name normalization, or executable scoring, extend the assertion in that same file rather than adding a framework.

To exercise the scanner on its own without launching the UI, compile `Services/GameScanner.cs` into a throwaway console project and print `GameScanner.InstalledGameProcessNames`. It is `internal`, so a separate assembly must include the source file rather than reference the exe.

## Shipping A Single Executable

```
dotnet publish -c Release
```

The result is one file at `bin\Release\net10.0-windows\win-x64\publish\AFK Assist.exe`. Copy it anywhere and run it — no .NET runtime on the target machine, no DLLs beside it, no install.

Five `.csproj` properties produce this, and all five are required:

| Property | Effect if removed |
| --- | --- |
| `SelfContained` | Target machine must have .NET 10 installed |
| `RuntimeIdentifier` | Cannot self-contain without a concrete target; `win-x64` here |
| `PublishSingleFile` | Publish folder fills with loose runtime DLLs instead of one file |
| `IncludeNativeLibrariesForSelfExtract` | WPF's native libraries land beside the exe instead of inside it |
| `EnableCompressionInSingleFile` | Exe roughly doubles in size |

Do not reach for trimming (`PublishTrimmed`) or Native AOT. WPF is not trim-safe and is unsupported under AOT; both either fail the build or produce an exe that crashes when the window is created.

`IncludeSourceRevisionInInformationalVersion` is `false` so `AssemblyInformationalVersion` stays a clean `5.0.0` instead of `5.0.0+<sha>`. The update checker parses that string.

## Formatting

Run this after every change, before reporting the work as done. It is not covered by `dotnet build`.

**CSharpier** formats all C#. It is installed as a global tool and invoked as `csharpier`, not `dotnet csharpier`:

```
csharpier format .
```

CSharpier also rewrites the `.csproj`, which is expected. Do not fight it by hand-indenting project files.

## Project Layout

```
AFK Assist.csproj / .slnx
App.xaml, App.xaml.cs           Application entry, WPF-UI theme dictionaries
Views/MainWindow.xaml(.cs)      The only window
ViewModels/MainViewModel.cs     All application logic
Services/InputSimulator.cs      SendInput keyboard and mouse injection
Services/GameWindowFocus.cs     Finds and foregrounds a running game window
Services/GameScanner.cs         Finds installed games on disk
Services/SimulationSchedule.cs  When inside a minute each action fires
Services/UpdateChecker.cs       GitHub latest-release comparison
Services/UserSettings.cs        Configuration persisted to %AppData%
```

There is no dependency injection container, no navigation, no messenger, and no repository layer. One window, one view model, six services. Keep it that way.

`UserSettings` is a record with `Load` and `Save` on it rather than a static class, because the thing being loaded and saved is the record itself. It writes `%AppData%\AFK Assist\settings.json` from the window's `Closing` handler and both directions swallow every exception — a settings file must never stop the app opening or closing. The Azerty flag is deliberately not persisted: it is detected from the active keyboard layout on every launch.

## Threading Model

Everything runs on the WPF dispatcher. `MainViewModel` starts its loops from a UI-thread command handler and never calls `ConfigureAwait(false)`, so every continuation comes back to the dispatcher. That is why there is no `Invoke`, no `Dispatcher.CheckAccess`, and no locking anywhere in the view model.

Two exceptions, both deliberate:

- `GameScanner.BeginScan()` runs the disk scan on the thread pool. It touches no UI state and publishes through a `Lazy<T>`.
- `Task.Delay` for key and mouse holds is awaited **without** a cancellation token. If Stop cancelled a hold mid-press, the key would never come back up and would stay stuck down inside the game. The hold is at most 160 ms, so letting it finish is correct.

`Start()` fire-and-forgets `RunAsync` with `_ = `. That is intentional: an async `[RelayCommand]` blocks re-entry while it runs, which would make the Pause button dead for the whole session. `RunAsync` catches everything itself, so nothing escapes.

## Input Injection

`InputSimulator` calls `SendInput` with a 40-byte `INPUT` struct. Both `wVk` and `wScan` are filled on every keystroke and `KEYEVENTF_SCANCODE` is **not** set. That matters:

- Games reading virtual keys (`WM_KEYDOWN`) see the correct letter for the layout.
- Games reading raw input or DirectInput see the correct physical scan code.

Setting only one of the two breaks half the games. Version 4 used `keybd_event` with scan code `0x45` and `KEYEVENTF_EXTENDEDKEY` for every key — `0x45` is NumLock, so raw-input games received garbage. Do not go back to `keybd_event`; it is a legacy wrapper that calls `SendInput` anyway.

Scan codes stay on the physical WASD positions (`0x11`, `0x1E`, `0x1F`, `0x20`) regardless of layout. Only the virtual key changes when Azerty is on, to `VK_Z` and `VK_Q`.

Azerty is detected with `VkKeyScanExW('a', GetKeyboardLayout(0))` — on Azerty the letter `a` sits on the physical `Q` key. This is layout-name-free and culture-free, so it does not care how Windows spells "Belgian". `InputLanguageManager.Current.InputLanguageChanged` re-runs it when the user switches layout, though that only fires while this app has focus.

`SendInput` delivers to whatever window is foreground. A mouse click therefore moves focus to whatever is under the cursor, which can pull keystrokes away from the game. That is inherent to the feature, not a bug — it is why the README says to keep the cursor inside the game window.

## Scheduling

`SimulationSchedule.CreateForOneMinute` divides the minute into one slot per action and places each action at its slot centre plus up to ±35% of a slot width of jitter, then sorts. This guarantees the exact requested count per minute and a minimum gap of 30% of a slot, with no rebalancing pass. When Randomize Intervals is off the jitter is zero and actions land exactly 60/n seconds apart.

`RunScheduleAsync` rebuilds the schedule at every minute boundary and polls at 250 ms. The poll exists so Pause and Stop stay responsive; a single long `Task.Delay` to the next action would sleep through both.

The first action of a run lands roughly 800 ms late because the schedule grid starts with the stopwatch but the loop waits out `FocusSettleDelayMilliseconds` first. Steady state is exact. Do not "fix" the first interval by starting the stopwatch later — the duration would then exclude the focus time.

## Game Detection

`GameScanner` is ported from the Adrenalize repository, which is the reference implementation. It finds installed games by walking the disk:

- **Steam** — parses `libraryfolders.vdf` for library roots, then every `appmanifest_*.acf` for `installdir`
- **Epic** — reads `InstallLocation` from each `.item` manifest under `C:\ProgramData\Epic`
- **Riot** — the two well-known roots plus anything in `RiotClientInstalls.json`
- **Rockstar** — `HKLM\SOFTWARE\Rockstar Games` install locations plus both Program Files roots
- **Roblox** — the version folder containing `RobloxPlayerBeta.exe`
- **Common roots** — `C:\Games`, `D:\Games`, `E:\Games`

Inside each root it picks the main executable by score: `win64` and `shipping` earn 3 points each, `launcher` costs 5, an exact folder-name match earns 4 and a partial match 2. Names containing `helper`, `service`, `crash`, `report`, `uninstall`, or `setup` are vetoed outright before scoring, so the runner-up survives.

Executable names are normalized to a process key by lowercasing and stripping `_` and `-`, with two hard-coded aliases for games that ship under many names (Assetto Corsa, Valorant).

`GameWindowFocus` matches that key set against running processes and foregrounds the first match, or the one process the user picked in the Options combo box. It returns the matched process name so the log can say which game it grabbed, and `null` when nothing matched. Every discovery source and every scoring rule is a faithful port — if detection needs changing, change it in Adrenalize first and port it back, do not fork the logic.

The scan takes well under a second and is cached for the process lifetime. Games installed while the app is running are not picked up; that is fine.

Foregrounding another process's window needs the `AttachThreadInput` dance, because Windows only grants foreground to a thread that already owns it. `BringWindowToTop` before `SetForegroundWindow` is part of that working recipe. Leave it alone unless you have tested the replacement against a real game.

## Update Checking

`UpdateChecker` issues one non-redirecting `GET` to the GitHub `releases/latest` URL and reads the tag out of the `Location` header. No API token, no JSON, no rate limit worth worrying about. It swallows every exception and reports "no update" — a failed check must never interrupt a run.

The startup check is silent when up to date. The titlebar button reports either way.

## UI Conventions

The window is `ui:FluentWindow` with `ExtendsContentIntoTitleBar` and a Mica backdrop. `SystemThemeWatcher.Watch(this)` plus `ApplicationThemeManager.ApplySystemTheme()` in the code-behind make it follow the Windows light/dark setting.

Use MudBlazor-equivalent thinking for WPF: reach for a WPF-UI control first (`ui:Card`, `ui:Button`, `ui:TextBlock`, `ui:ToggleSwitch`, `ui:NumberBox`, `ui:InfoBar`), then a plain WPF control that WPF-UI restyles (`CheckBox`, `Slider`, `ListBox`, `ProgressBar`), then a `Style` or inline property. Custom `ResourceDictionary` entries are the last resort and there are currently none.

XAML comments use `<!-- Name -->` and sit above a group of elements, naming the main element of that group only. One attribute per line, aligned under the first.

The Activity log binds to `ObservableCollection<LogEntry>`, not to strings. Each entry carries its own `LogKind`, and the `DataTemplate` colours the message from it through `DynamicResource` theme brushes so the palette follows light and dark. The timestamp is a separate `Auto` column rather than padding inside one string. The `ListBoxItem` template is replaced outright because WPF-UI's own template hardcodes a square hover highlight and there is no `CornerRadius` to override.

Two things in the XAML are load-bearing:

- **The `Duration` number boxes bind with `UpdateSourceTrigger=PropertyChanged`.** `ui:NumberBox.Value` defaults to `LostFocus`, and neither the spin buttons nor a click on `ui:Button` move keyboard focus out of the box, so the typed or spun value never reached the view model and the run used whatever was there before. `DurationHours` and `DurationMinutes` are `double?` for the same control: an emptied box reports `null`, and a non-nullable target would silently leave the old value in place. Both call sites coalesce with `?? 0`.
- **The Input card and the Options card sit in one shared `Auto` row, both `VerticalAlignment="Stretch"` and `VerticalContentAlignment="Stretch"`.** That is what makes the two top cards the same height and starts their headers on the same line. `ui:Card` sets `VerticalContentAlignment="Center"` in its default style, so the shorter card floats its content down the middle and its header sits a few pixels low; setting the property back to `Stretch` is what fixes it, and setting it to `Top` does not. Binding one card's `Height` to the other's `ActualHeight` also works but then centres the card itself inside the row, which needs a second fix — the shared row needs none.

Three things in the code-behind are load-bearing:

- **Log autoscroll must be posted.** `LogListBox.ScrollIntoView` called synchronously from `CollectionChanged` throws once the item generator is mid-update. That exception used to propagate out of `LogEntries.Add`, out of `AppendLog`, and silently kill the simulation loop — the log froze, `Finished` never appeared, and the presses stopped. It is now wrapped in `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)`. Do not inline it again.
- **The view model is constructed in the code-behind**, not in XAML. `d:DataContext` is design-time only.
- **`MakeRoomForNotice` moves `MinHeight`, never `Height` alone.** The window is sized so the configuration column exactly fills it with nothing to spare, which leaves the `ui:InfoBar` no room to open into — without this the column scrolls and cuts the Duration card in half. It adds the bar's height to the base `MinHeight` captured at construction and only drags `Height` along while the window is still sitting at its old minimum, so a window the user enlarged is left alone. The earlier version added and subtracted deltas from `Height` and drifted a little further open on every toggle; recomputing an absolute target is what makes it idempotent.

The notice `ui:InfoBar` is not exposed to UI Automation, so automated checks cannot read its message. Verify it with a screenshot instead.

## Verifying Changes By Hand

There is no UI test harness. What works:

- **Screenshots without stealing focus** — `PrintWindow` with flag `2` (`PW_RENDERFULLCONTENT`) against the window handle. Call `SetProcessDPIAware` in the capturing process first, or `GetWindowRect` returns virtualized coordinates and the capture comes out cropped.
- **Driving the UI** — `System.Windows.Automation` from Windows PowerShell. Buttons expose `InvokePattern`, checkboxes and `ui:ToggleSwitch` expose `TogglePattern`, the slider exposes `RangeValuePattern`, and `ui:NumberBox` exposes `ValuePattern`.
- **Confirming keystrokes actually landed** — count characters in a focused Notepad. Do not count log lines: the `ListBox` virtualizes, so UI Automation only sees realized rows and undercounts once the log scrolls.

Running the app takes over the machine — it is `Topmost`, it maximizes and foregrounds a detected game, and it injects real input. Ask before testing if there is any chance a game is running.

## Comment Style

Comments are very short and clear, a couple of words, at most eight to ten when genuinely needed. Never multi-line. Every word starts with a capital letter. No ending punctuation. Always on their own line above the code they describe.

In C#, comments are allowed only inside method bodies or above a group of related fields or properties. No XML documentation comments. No comments on view models, models, services, interfaces, or class declarations. No comments in `.csproj` files. Never comment obvious code, and never write a comment that explains something to the reader of this file rather than to the reader of the code.

In markup, comment only above a group or chunk of elements, and the comment holds the name of that group's main element and nothing else. XAML uses `<!-- Name -->`, Razor uses `@* Name *@`. Never comment above text, parameters, bindings, individual attributes, or small markup fragments, and never write a descriptive markup comment.

```
@* Records Table *@
<MudTable>
...
</MudTable>
```

## Code Style

Full descriptive names everywhere for variables, fields, properties and methods. No abbreviations, no single letters. `settings`, not `s`; `configuration`, not `cfg`; `cancellationToken`, not `ct`. This covers regex capture groups and lambda parameters too.

`var` for locals, which is what `.editorconfig` asks for and what every file already does. Write the type out only when the right-hand side does not carry it: a target-typed `new()`, a collection expression, a declaration with no initializer, or a bare `null`.

```csharp
var executableName = Path.GetFileNameWithoutExtension(executablePath);
HashSet<string> processNames = new(StringComparer.OrdinalIgnoreCase);
List<string> roots = [];
string? bestExecutablePath = null;
```

Descriptive names matter here, `var` does not hide anything the name should have carried.

`#region` blocks wrap methods only, never fields or properties. No blank line directly after a `#region` or directly before an `#endregion`; one blank line before the `#region` and one after the `#endregion`. Only add regions when there would be two or more.

UI strings follow the comment rules: short, every word capitalized, no ending punctuation. This covers labels, titles, helper text, validation messages and localization text alike. "Select At Least One Input", not "Please select at least one input."

Control choice runs down a ladder, first rung that works wins. In Blazor that is MudBlazor, then Bootstrap classes, then a MudBlazor component's `Style`, then custom CSS; `MudElement` beats a plain HTML element, and plain HTML is the last resort. The WPF equivalent is in [UI Conventions](#ui-conventions).

Razor components keep their logic in the code-behind. Never open an `@code` block in a `.razor` file when a `.razor.cs` exists beside it.

## Before Making Changes

Read every file the change touches first — code-behind, services, models, interfaces, registrations and the project file. Do not assume the architecture.

## Things That Look Wrong But Are Not

- `ProgressPercentage` is set to 100 in `Finish` only when the run completed, and left alone when it was stopped, so a stopped bar shows how far it got. Neither branch resets it, because `Start` calls `UpdateTimeLabels` against a fresh stopwatch one line later.
- `Stop` is bound to `CanExecute = nameof(IsRunning)` while Start/Pause has no `CanExecute`, so the primary button stays live to accept Pause.
- `catch { }` with an empty body appears several times in `GameScanner`. Disk and registry probes for paths that may not exist are expected to fail, and a failed probe means "not installed".
- `EnumerateSafely` wraps the enumerator by hand rather than using a `try`/`catch` around a `foreach`, because directory enumeration is lazy and throws on `MoveNext`, not on the call that creates it.
