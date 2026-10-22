# MacroStudio 2.0

A local Windows 11 desktop macro recorder built with C#, WPF, MVVM and .NET 10 LTS. It records system-wide mouse/keyboard input using low-level Windows hooks, groups mouse paths, and replays with `SendInput`.

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then run in PowerShell:

```powershell
./scripts/build.ps1
dotnet run --project src/MacroStudio -c Release
```

The script also accepts a project-local `.dotnet/dotnet.exe`. NuGet restores the pinned OpenCvSharp 4.13 local image-matching dependency. Local OCR uses installed Windows OCR languages (the verification fixtures require en-US). `./scripts/build.ps1 -Package` builds, tests and publishes a self-contained x64 distribution to `artifacts/MacroStudio/` plus `artifacts/MacroStudio-win-x64.zip`. Launch `MacroStudio.exe` there. The included Interaction Lab is under `Lab/` and opens from the toolbar. ARM64 requires the matching OpenCvSharp native package and separate validation; this distribution is x64.

The updated release for this workspace is `artifacts/MacroStudio-2.0/MacroStudio.exe`; extract its full `artifacts/MacroStudio-2.0-win-x64.zip` before running. Reproduce it with `./scripts/build.ps1 -Package -OutputDirectory artifacts/MacroStudio-2.0`. The .NET runtime and native image-matching library are included; a clean Windows installation may also need Microsoft's x64 Visual C++ redistributable. Close the chosen package before rebuilding it.

## Use

1. Open **Interaction lab** or another target app. Press **Record** (default **F8**), perform the task, then press **Esc** to stop. Recording is explicit and has a visible compact controller.
2. Select a step and edit its delay, duration, text and comment in the right panel, then **Apply properties**. **Edit properties** opens coordinates, keys, paths and window targets. Insert opens the same editor with all manual action types available. Down and up are separate actions; two click pairs reproduce a double-click and a held pair surrounds a drag path.
3. Save as local `.json`, reopen with File → Open, then **Play** (**F9**). **F10** pauses/resumes; **Esc** cancels waits and repeats and releases engine-held input. Shortcuts are configurable under **Shortcuts**, with duplicate/OS registration conflict detection.

Playback speed accepts 0.05–100×, repeats 1–1000000, or until stopped. Options include skipping mouse paths and restoring recorded target-window bounds. The inspector offers one-action, selected-range (inclusive first-to-last) and from-selection playback. One-action playback runs once. Playback has an initial cancellable one-second delay; recorded delays are retained.

Multi-select with Ctrl/Shift. Drag selected rows to move them before another row, or use ↑/↓. Ctrl+X/C/V/D cuts, copies, pastes and duplicates actions; Delete removes them. Enable/disable, undo/redo (100 snapshots), comments and search work locally. Ctrl+N/O/S manages files and Ctrl+Z/Y history. File operations prompt for unsaved changes, preserve recent files and validate loaded data.

Desktop coordinates are physical pixels across the virtual desktop, including negative monitor coordinates. Window-relative recording stores physical offsets from the target's **outer window rectangle**, not client-area DIPs. Targets store process names and titles, never HWNDs. Playback uses exact title, unique partial title, or a unique process window; ambiguity produces an error. Layout changes within an app can require editing or rerecording coordinates. Choose window-relative recording before Record.

The compact controller uses `WS_EX_NOACTIVATE` and does not steal the target's keyboard focus. MacroStudio-owned windows and control shortcuts are omitted. Its own tagged playback input is ignored by hooks. Input from other automation/accessibility tools can be recorded. Keep only one MacroStudio instance running.

Macros stay wherever you save them. Theme, shortcuts and up to ten recent paths are stored in `%LOCALAPPDATA%/MacroStudio/preferences.json`. Recording captures keys: avoid typing secrets while recording. The app has no telemetry. Optional AI requests happen only when you configure/enable a provider and explicitly play an AI action. All local features work without an account or key.

Windows restricts input into elevated apps and secure desktops. Use the same privilege level for target and recorder; injection failures explain this limitation. Windows can also refuse focus changes. A secure desktop, locked session or unavailable target cannot be automated. Stop reports cleanup failures if Windows prevents releasing input.

## Verification

`./scripts/build.ps1` runs 17 core, seven editor and 23 advanced checks. `./scripts/verify-native.ps1` publishes a separate lab and runs nine opt-in native checks: activate the freshly launched lab, then leave mouse/keyboard input alone until it completes. It records test input only, writes evidence under `work/native-verification`, aborts its input if the lab loses focus, and closes the lab when done. Run on an unlocked interactive desktop. Middle clicks inside a text editor can activate autoscroll; the test uses the counter button for auxiliary clicks.

See [verification](docs/VERIFICATION.md) for checks actually run and hardware-dependent checks, [format](docs/FORMAT.md) for the open JSON format, and [implementation plan](docs/IMPLEMENTATION-PLAN.md) for architecture and sources.

## Advanced automation

Use **Insert / Edit action** to configure local image-targeted clicks, conditional waits, screenshots, OCR, typed variables, clipboard/text/CSV data, labels/branches/repeats and reusable macro calls. Open **Inspector** for paused variables, single stepping and searchable execution logs. Adjacent named groups collapse in the action list; mouse paths have recorded, linear and curved playback plus previews.

**Visual recording** uses fresh background captures completed before simple click edges; missing context requires a template before replay. Image clicks stop on failure by default. Coordinate fallback is explicit. Matching is exact scale: recapture after DPI, zoom, theme or target appearance changes. Low-detail and ambiguous templates are rejected.

**Library** provides folders, name/path search, favorites and per-macro global hotkeys. File menu portable export/import includes referenced images and reusable macros. Existing v1 files remain readable. Command-line playback and Windows Task Scheduler setup are documented; input/capture runs require an unlocked interactive desktop.

**AI provider** supports opt-in schema-capable compatible chat endpoints and native Ollama, explicit text/image actions, cancellation/timeouts and validated typed output. Credentials use Windows Credential Manager. No model-generated prose is converted into executable macro instructions.

Read the [advanced guide](docs/ADVANCED.md), [CLI and scheduler setup](docs/SCHEDULER.md), [2.0 verification and measured limitations](docs/VERIFICATION-2.0.md), and [third-party components](docs/THIRD-PARTY.md).

`./scripts/verify-advanced.ps1 -PackageDirectory artifacts/MacroStudio-2.0 -Credentials` runs the opt-in moved/delayed/absent target scenario, real OCR-to-text-entry pipeline and a temporary synthetic Credential Manager round trip. Activate the newly opened Visual Lab and leave input idle. Escape cancels; losing foreground aborts injected input. WPF view render artifacts are reproducible with `dotnet run --project tests/MacroStudio.EditorTests -c Release -- --render`.
