# Verification and requirement audit

Environment: Windows x64, .NET SDK 10.0.402 / desktop runtime 10.0.13, 2560 × 1600 virtual desktop, 150% DPI. The app and lab compile with zero warnings/errors. The release is self-contained.

## Checks actually run

- **17/17 core checks:** all action-kind JSON/Unicode/negative-coordinate round trips; malformed/unsupported/invalid documents; atomic replacement/reopen; exclusive states; speed/timing/negative-desktop normalization; path and drag ordering; control/modifier filtering; start-transition cancellation; independent startup delay; selection modes and snapshot isolation; disabled actions/repeats; wait/repeat/paused cancellation; failure cleanup.
- **7/7 editor checks:** insert/edit/dirty/saved baseline; undo/redo; multi-selection duplication/fresh IDs; reorder/move order and selection; enable/disable/search/delete/history; playback options/history; a native controller handle whose WS_EX_NOACTIVATE, MA_NOACTIVATE response, ShowActivated=false and unchanged foreground HWND are asserted.
- **9/9 native checks:** actual low-level hooks and SendInput against a separate Interaction Lab. Tests cover duplicate/real OS shortcut conflicts; three buttons, wheel, grouped drag path, Shift+A and control exclusion; target-side effects; edit → atomic save → reopen → replay reproducing text/counter/scroll/drag; tagged-input exclusion; stop during a 60000 ms delay with real Ctrl and left-button states checked before/after; stop after at least three infinite repetitions; global pause/resume/stop with the lab focused; Unicode plus relative coordinates/restoration at 150% DPI.

Native evidence is in `work/native-verification/results.json`; `recorded-edited-reopened.json` is the actual 26-step capture. Assertions inspect target-side telemetry, not just SendInput return values. The lab's optional `--probe` is a read-only local file sink with no command receiver or network listener. The production app has no telemetry.

Historical failed attempts identified cold-start timeout, DPI-unaware test hosting and middle-click autoscroll in a text editor. Those were corrected. The passing results file is authoritative; an older failure.txt is historical.

The actual WPF app was built and launched. A separate lab recording captured Shift+A, two counter clicks (including a double-click), paths and held drag. Global Escape stopped it. The editor displayed 18 steps; its initial delay was changed from approximately 8568 ms to zero and saved through the native Save dialog as `work/desktop-recording.json`. The newer UI reopened that file through Recent files with the zero delay intact. The right-hand editable inspector and theme controls were inspected. A read-only-checkbox binding crash was fixed; subsequent captures rendered successfully. Final-package smoke observations are recorded in `work/release-checks.md`.

The final self-contained release was launched and opened the included 26-step recorded lab sample. Its main Play button reproduced text A, counter 2, scroll offset 48 and the drag target, then returned to Idle/repetition 1 with "Playback complete". Final light/dark themes, immediate speed-change dirty tracking, Undo and clean app/lab shutdown were observed. The release ZIP and source ZIP are verified member-for-member by `scripts/verify-package.py`; the evidence report is `work/package-verification.json`.

## Requirement map

| Requirement | Implementation and evidence |
| --- | --- |
| Windows 11/C#/WPF/MVVM and separate layers | Core, Windows, app and lab projects; successful real Windows build/launch; plan/API sources in IMPLEMENTATION-PLAN.md. |
| Input types, modifiers, double-clicks, drags, timing | HookService → queued input → RecordingService. Native target-side checks and desktop capture; down/up edges and timestamped paths preserve held input and double-click spacing. |
| Unicode/manual actions | KEYEVENTF_UNICODE; editor offers all eight action kinds, paths/chords/coordinates/targets. Native Unicode assertion, serialization tests, inspected UI. |
| Meaningful paths/light callbacks | Callbacks marshal/enqueue; worker resolves metadata and assembles paths. Native multi-point path and ordered-offset tests. |
| Controls/self-injection exclusion | HookService excludes its process windows and tagged input; assembler filters control chords/unused modifiers. Focused filter tests, toolbar capture and native control/tag exclusion. |
| Configurable global controls/conflicts | Four parsed/registered shortcuts; actual duplicate and OS conflict checks; global stop/pause/resume assertions. |
| Speed/repeat/path filter/selection playback | Options, PlaybackSelection and PlaybackEngine; timing/order/repeat/selection tests and native infinite-repeat stop; UI settings. Skipped paths retain timing; click/scroll coordinates still apply. |
| Emergency stop/held-input cleanup | Cancellable monotonic waits/loops and finally ReleaseAll. Native Ctrl/button cleanup, long wait/repeat/paused stop; fault test. |
| Desktop/relative coordinates, stable windows/restoration | Physical offsets and process/title matching with ambiguity errors; HWNDs are never serialized. Native moved-window restoration and relative entry test. |
| Monitors/negative coordinates/DPI | PerMonitorV2, explicit DPI scopes, virtual-desktop metrics and VIRTUALDESK normalization. Negative-coordinate tests and real 150% recording/replay; hardware limits below. |
| Open versioned JSON, validation/atomic save/unsaved/recent | MacroStore, FORMAT.md and file workflow. Roundtrip/validation/atomic tests, native save/reopen, desktop Save and Recent reopen. |
| UI/editor operations/status | Compact toolbar, central grid, right inspector, bottom status, themes; selection/history/search/clipboard/reorder tests. Running GUID/repetition feeds row highlight/status. Errors appear in status/dialog. |
| Compact nonactivating controller | ShowActivated=false, WS_EX_NOACTIVATE, MA_NOACTIVATE and Topmost; native handle/focus assertions. |
| Responsiveness/disposal/errors | Playback off UI dispatcher, Stopwatch clock, 10 ms cancellable wait slices, unhooks in finally/Dispose, SendInput checks with privilege guidance; native and fault/cancellation tests. |
| Test target/source/build/samples/instructions | Lab text fields/counter/scroll/drag; build and native scripts; samples, README and format specification. |

## Hardware checks not run

The tested desktop used 150% DPI. Moving windows between monitors with different scaling, physical monitors left/above primary, alternate keyboard layouts/IMEs, elevated targets and secure desktops were **not** exercised on hardware. Coordinate/API handling and errors are implemented and inspected; those environments are not claimed as hardware-tested. Verify the intended environment before use. Coordinate targeting requires a matching app layout. Windows focus/privilege restrictions are reported rather than bypassed.

Visual targeting, OCR, branching, scheduling and AI remain outside this core version.
