# Game selection binding validation

Production changes in `AFKAssist.Desktop/Components/Home.razor`:

```diff
-                                           Value="MainViewModel.SelectedGameKey"
+                                           Value="@MainViewModel.SelectedGameKey"
-                                        <MudSelectItem Value="game.Key">@game.Value</MudSelectItem>
+                                        <MudSelectItem Value="@game.Key">@game.Value</MudSelectItem>
```

The existing ValueChanged callback, GameNameOf label lookup, disabled condition,
settings handling and game-window activation code are unchanged.
No test project or package dependency was added.

## Executed checks (Linux, 4 November 2026)

From the repository root:

```sh
python3 scripts/check_game_selection.py
```

Result: exit 0; all three tests passed (different synthetic games, restored
model-side selection, and Automatic's empty key).
This is a source-contract check using a limited model of Razor string parameters,
not a Razor compiler, component interaction test or execution of MainViewModel.
No real games, window activation or keyboard/mouse injection were used.

Negative controls were executed with this exact command:

```sh
python3 -B - <<'PY'
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('check', 'scripts/check_game_selection.py')
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)
source = check.HOME.read_text()
mutations = {
    'original bindings': source.replace('Value="@MainViewModel.SelectedGameKey"', 'Value="MainViewModel.SelectedGameKey"').replace('Value="@game.Key"', 'Value="game.Key"'),
    'literal selected value': source.replace('Value="@MainViewModel.SelectedGameKey"', 'Value="MainViewModel.SelectedGameKey"'),
    'literal item key': source.replace('Value="@game.Key"', 'Value="game.Key"'),
}
with tempfile.TemporaryDirectory() as directory:
    check.HOME = Path(directory) / 'Home.razor'
    for name, mutated in mutations.items():
        check.HOME.write_text(mutated)
        result = unittest.TextTestRunner(stream=io.StringIO()).run(unittest.defaultTestLoader.loadTestsFromTestCase(check.GameSelectionContract))
        if result.wasSuccessful() or result.errors:
            raise SystemExit(f'Unexpected result: {name}')
        print(f'PASS: rejected {name} ({len(result.failures)} assertion failures)')
PY
```

Result: exit 0; original bindings rejected with 4 assertion failures,
literal selected value with 6, literal item key with 4. No test errors.
These mutations only affect a temporary file.

```sh
dotnet build AFKAssist.Desktop/AFKAssist.Desktop.csproj -p:EnableWindowsTargeting=true
```

Result: exit 127, `dotnet: command not found`. Compilation was not verified.

## Still required on Windows

- Build with the .NET 10 SDK and the project's existing package versions.
- In WPF/BlazorWebView with MudBlazor, use synthetic AvailableGames entries
  (empty key/Automatic, synthetic:alpha/Alpha, synthetic:beta/Beta). Select
  Alpha, Beta, then Alpha; verify the displayed label and SelectedGameKey.
- Seed a previously selected synthetic key before the list loads; verify its
  label after loading and rerendering. Separately verify real settings save/load
  in an isolated profile, plus the missing-key fallback to Automatic.
- Verify Automatic initially and after an explicit selection, including its
  empty SelectedGameKey. Verify the selector is disabled while locked and when
  Switch To Game is off.
- With disposable dummy windows and input simulation stubbed/disabled, verify
  explicit selection reaches the matching window, Automatic resolves a window,
  and automatic selection resets on stop. Do not run the normal simulation loop
  against real games or inject keyboard/mouse input during these checks.
