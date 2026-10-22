# MacroStudio implementation plan

1. Model explicit down/up input, timestamped mouse paths, Unicode text, delays and stable process/title window targets. Validate and atomically persist versioned local JSON.
2. Install Windows low-level keyboard/mouse hooks on a message-pump thread. Queue events, aggregate paths off-hook, exclude our windows/control shortcuts/injected events, and use a single session state machine.
3. Play with SendInput, physical virtual-desktop coordinates, window-relative offsets, cancellable monotonic scheduling, pause and held-input cleanup.
4. Build a WPF/MVVM editor, accessible light/dark theme, command toolbar, editable inspector, history, clipboard, search, selection playback and a nonactivating controller.
5. Build a local interaction lab, focused dependency-free test runner, real desktop integration checks, samples and a reproducible Windows packaging script. Record evidence and remaining hardware-dependent checks honestly.

References reviewed before implementation: [documentation](https://www.macrorecorder.com/doc/), [screenshots](https://www.macrorecorder.com/screenshots/), [playback](https://www.macrorecorder.com/doc/playback/). These informed path grouping, playback selection, window restoration and filtering. The interface is original.

Native API references: Microsoft [SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw), [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [SetThreadDpiAwarenessContext](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext). Use low-level hooks, not Windows 11's unsupported journal hooks. SendInput is subject to UIPI.
