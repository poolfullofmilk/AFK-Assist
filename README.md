# AFK Assist

Keep Your Game Active While You Are Away By Simulating Keyboard And Mouse Input

![AFK Assist](Screenshot-Idle.png)

![AFK Assist Running](Screenshot-Running.png)

## Features
- Keyboard (WASD Or Chosen Keys) And Mouse (Left/Right) Simulation
- Adjustable Speed (1-10 Simulations Per Minute) And Duration (Up To 8 Hours)
- Duration Presets For 15m, 30m, 1h And 8h
- Optional: Randomized Patterns, Randomized Intervals, Auto Focus To Game Window, Azerty Auto Detection
- Picks Any Installed Game To Focus, Or Finds A Running One Automatically
- Live Activity Log With Elapsed And Remaining Timers
- Settings Are Remembered Between Sessions
- Pause, Resume And Stop Anytime

## Quick Start
1. Choose Mouse Clicks
2. Choose Keyboard Keys
3. Choose Speed
4. Choose Duration
5. Press Start

## ⚠️ Important
- Game Support For Virtual Input Varies, Some Games Only Count Mouse Activity Against Their AFK Timer
- Keep The Game Window Focused And The Cursor Inside It For The Simulation To Work

## Technical Details
- Windows App Built With C# And WPF On .NET 10
- Fluent Interface Through WPF-UI, MVVM Through CommunityToolkit.Mvvm
- Input Is Injected With The Win32 SendInput API Using Both Virtual Keys And Scan Codes

## Download
Get The Latest Version From The [Latest Release](https://github.com/yusuftuncay/AFK-Assist/releases/latest)
