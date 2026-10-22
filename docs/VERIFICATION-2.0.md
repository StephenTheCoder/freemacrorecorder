# MacroStudio 2.0 verification — 2026-10-22

This records evidence from the implementation session. The 1.0 release is preserved separately; the updated runnable package is `artifacts/MacroStudio-2.0-win-x64.zip`.

## Checks run

| Group | Evidence |
| --- | --- |
| Core baseline | 17 checks: serialization/validation, v1 coordinate/timing semantics, state transitions, selection playback, repeats, pause/stop, held-input cleanup and recording assembly. |
| Editor baseline | Seven checks: controller no-activation, insert/edit, undo/redo, duplicate IDs, reorder/selection, search/enable/delete and option validation/history. |
| Advanced deterministic/local | 23 checks: shifted/boundary/absent/ambiguous/low-detail image fixtures, unsupported scaling/cancellation, visual-click assembly, fresh-context failure behavior, typed substitution/bindings, arithmetic/comparisons, quoted multiline CSV, text/CSV file round trips, actual local Windows OCR into a typed variable and a fake text sink, flow/timeout policies, preflight failures, recursion/nested cancellation, debugger stepping, path shapes, portable assets/calls after source removal, traversal rejection, library persistence, provider protocol/schema/disabled/cancellation/timeout tests and v1 file compatibility. |
| Original native recording/playback | Nine checks rerun against the updated engine on the real Interaction Lab at 150% DPI. Real hooks captured mouse paths, three buttons, drag, scroll and Shift+A; edited/saved/reopened replay reproduced lab state. Tagged input was excluded; real global stop released Ctrl/button, stopped an infinite repeat and cancelled a long delay; pause/resume and Unicode/window-relative restoration passed. `work/advanced-native-regression/results.json`. |
| Advanced native scenario | A target captured at one position moved by 415 DIPs and was clicked at its new location. The next click waited for its 2.5-second delayed reappearance. Disappearance, region-change, pixel and window waits passed. The absent target timed out with no extra click. Actual screenshot → local OCR → text variable → branch → actual lab text entry and unique OCR position passed. `work/advanced-verification/visual-result.json`, `ocr-capture.png`. |
| Credential storage | Synthetic credential saved/read/deleted using Windows Credential Manager in the interactive user session. A sandbox-only attempt returned error 1312 (no logon session); the interactive check passed. No user API key was accessed or transmitted. |
| Packaged GUI | The self-contained GUI showed its main window and shut down cleanly with exit code 0. `work/advanced-verification/gui-smoke.json`. |
| CLI | Separate processes returned **0** for a valid v1 document, **2** for a missing label, and **4** for a one-second outer timeout on a 60-second delay-only macro with a saved breakpoint. Corresponding logs are under `work/advanced-verification/cli-*.log`. No input was injected by those three CLI probes. A further separate packaged CLI process successfully ran native image matching, screenshot capture, local OCR and text-file output against the visible lab (`packaged-cli.log`); no target input was injected by that CLI process. |
| WPF layout | Actual WPF view trees rendered at 1320×800 and 1000×700 plus action, detection, data, flow, inspector and provider dialogs; artifacts inspected under `work/advanced-ui`. Fixed collapsed/clipped columns, missing Step control, irrelevant action fields and window-theme inheritance. |

Default build checks total **47** (17 + 7 + 23). The nine native regressions plus advanced live scenario and credential round trip bring the executed check count to **58**, excluding the separate CLI/GUI probes and visual review.

## Measurements and discovered issues

- Test desktop: virtual 2560×1600 physical pixels, display scale **1.5 / 144 DPI**. Visual button template: **255×105** physical pixels. OCR test text was `AUTOMATION READY 42`.
- The latest recorded absent-target run configured **350 ms** and returned after **449 ms**. Deadline checks run between matching strips as well as polls; this includes the remaining capture/strip overhead. A preceding whole-scan implementation measured 1297 ms under load and was corrected. It stopped with the lab click count unchanged at two.
- A delayed-target attempt initially failed because default WPF hover artwork changed after the first click. The lab now uses stable artwork. This exposed a real exact-image limitation; matching arbitrary hover/theme/zoom changes is not claimed.
- Windows initially rejected automatic foreground activation. Native checks wait for the lab to be foreground and abort input when focus is lost. They do not force foreground through OS restrictions.
- Nested cancellation and detection cancellation tests assert sub-500 ms completion on the fixture workloads. Larger native image operations remain cancellable between strips, not within one OpenCV call.

## Remaining limits and unverified paths

- Desktop inspection via the computer-use helper was unavailable: its native pipe could not be connected, including recovery. Rendered real WPF views were inspected, and the separate native lab was exercised successfully. The desktop-driven drag-to-select picker, Test match outline, library hotkey launch and paused inspector interactions were not replayed end to end through that helper. They are implemented, with no placeholder actions, but these particular UI gestures remain unverified.
- The fresh-context visual recording path has deterministic assembly/failure tests. A physical visual-recording session followed by replay was not separately demonstrated in this session; ordinary physical recording/replay passed all nine native regressions.
- Clipboard read/write is implemented but was not exercised against the user's live clipboard to avoid disturbing it.
- No mixed-DPI multi-monitor hardware, negative-origin monitor arrangement, remote-session transition, alternate IME/layout, protected content, elevated/secure-desktop target or clean Windows installation was hardware-tested.
- Exact-scale visual matching rejects unsupported scale/DPI changes. Rotation, perspective, semantic target recognition and broad hover/theme adaptation are unsupported. Similar targets require a narrower region or better context.
- OCR is local Windows OCR; only the installed en-US recognizer and clear high-contrast text were exercised. Other languages, small text and complex layouts need separate verification.
- AI protocol/structured-output behavior was verified with in-process HTTP fixtures, not a running Ollama model or paid cloud provider. Models must support the selected protocol/schema and image inputs. AI remains disabled until configured; local features need no account.
- Task Scheduler integration is documented and CLI behavior was tested. No scheduled task was installed or allowed to run later. Locked/service/secure desktops are unsupported.
- The distribution includes the .NET runtime and OpenCV native library. A clean machine may need Microsoft's x64 Visual C++ redistributable. This prerequisite was present on the verified machine.

## Reproduce

```powershell
./scripts/build.ps1 -Package -OutputDirectory artifacts/MacroStudio-2.0
./scripts/verify-native.ps1 -EvidenceDirectory work/advanced-native-regression
./scripts/verify-advanced.ps1 -PackageDirectory artifacts/MacroStudio-2.0 -Credentials
dotnet run --project tests/MacroStudio.EditorTests -c Release -- --render
python scripts/verify-package.py artifacts/MacroStudio-2.0 --create --source
```

Native checks require an unlocked desktop. Activate the freshly opened lab and leave input idle until it closes. Do not run the two native suites concurrently. See [advanced behavior](ADVANCED.md) and [scheduler setup](SCHEDULER.md).
