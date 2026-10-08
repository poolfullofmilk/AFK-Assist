# AFK Assist

Keeps your game from kicking you while you are away.

![AFK Assist](Screenshots/Idle.png)

![AFK Assist running](Screenshots/Running.png)

## Features
- Presses keys, clicks and moves the mouse on a random, human-like schedule
- Can switch to your game and only type there, and pauses the moment you touch the PC
- Runs for a set time or until a clock time

## Quick start
1. Download the exe from the [latest release](https://github.com/poolfullofmilk/AFK-Assist/releases/latest)
2. Pick your inputs and press Start

## ⚠️ Important
- Anti-cheat can tell simulated input apart from real input

## Technical details
- C# and WPF on .NET 10, input through the Win32 `SendInput` API
