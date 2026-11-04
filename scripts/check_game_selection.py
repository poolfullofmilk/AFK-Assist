"""Source-contract regression check; run: python3 scripts/check_game_selection.py.

Uses synthetic keys to check the two Razor string expressions and the existing
write-back callback. This does NOT compile Razor, execute MainViewModel, load
settings, render MudBlazor, scan games, focus windows, or inject input.

Still required on Windows: build the desktop project, then use synthetic entries
in a debugger to check selection, restored selection after list loading, Automatic,
labels and disabled states in WPF/BlazorWebView/MudBlazor. Check window activation
separately with disposable dummy windows and input simulation disabled/stubbed.
"""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
HOME = ROOT / "AFKAssist.Desktop/Components/Home.razor"
GAMES = [("", "Automatic"), ("synthetic:alpha", "Alpha"), ("synthetic:beta", "Beta")]


def string_parameter(expression, values):
    # Only model the implicit string expressions used by this selector.
    # Unprefixed string parameters are literal; unknown expressions fail closed.
    return values[expression[1:]] if expression.startswith("@") else expression


class GameSelectionContract(unittest.TestCase):
    def setUp(self):
        source = HOME.read_text(encoding="utf-8")
        selectors = re.findall(r'<MudSelect\b.*?</MudSelect>', source, re.S)
        self.assertEqual(len(selectors), 1)
        self.selector = selectors[0]
        self.selected = re.search(r'\bValue="([^"]*)"', self.selector).group(1)
        self.item = re.search(r'<MudSelectItem\b[^>]*\bValue="([^"]*)"', self.selector).group(1)
        self.assertIn('T="string"', self.selector)
        self.assertIn('@foreach (var game in MainViewModel.AvailableGames)', self.selector)
        self.assertIn('>@game.Value</MudSelectItem>', self.selector)
        self.assertIn('ToStringFunc="GameNameOf"', self.selector)
        self.assertIn('ValueChanged="value => MainViewModel.SelectedGameKey = value"', self.selector)
        self.assertIn('Disabled="@(IsLocked || !MainViewModel.SwitchToGameEnabled)"', self.selector)

    def options(self):
        return [(string_parameter(self.item, {"game.Key": key}), label) for key, label in GAMES]

    def selected_value(self, key):
        return string_parameter(self.selected, {"MainViewModel.SelectedGameKey": key})

    def test_different_games_provide_distinct_keys(self):
        options = self.options()
        self.assertEqual(options, GAMES)
        for index in (1, 2, 1):
            with self.subTest(key=GAMES[index][0]):
                # The checked callback assigns this emitted value to the model.
                emitted_key = options[index][0]
                self.assertEqual(emitted_key, GAMES[index][0])
                self.assertEqual(self.selected_value(emitted_key), GAMES[index][0])

    def test_previously_selected_game_is_read_from_model(self):
        # Seed the model-side value without selecting an item first.
        for restored_key, expected_label in GAMES[1:]:
            with self.subTest(key=restored_key):
                selected = self.selected_value(restored_key)
                self.assertEqual(selected, restored_key)
                self.assertEqual(dict(self.options()).get(selected), expected_label)

    def test_automatic_uses_empty_key_initially_and_after_selection(self):
        self.assertEqual(self.selected_value(""), "")
        self.assertEqual(self.selected_value(GAMES[2][0]), GAMES[2][0])
        automatic_key, label = self.options()[0]
        self.assertEqual((automatic_key, label), ("", "Automatic"))
        self.assertEqual(self.selected_value(automatic_key), "")


if __name__ == "__main__":
    unittest.main(verbosity=2)
