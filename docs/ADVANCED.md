# MacroStudio 2.0 automation guide

The v1 mouse/key recorder, physical/window coordinates, hotkeys, speed/repeats, editing, undo/redo and local JSON files remain available. Existing v1 files load in this version. Open **Visual lab** for a separate target with a movable button, delayed appearance, show/hide controls, OCR text and a text-entry field.

## Visual clicks and waits

Insert an action, choose **ImageClick**, then **Configure visual target / capture region / wait**. **Capture template** freezes the desktop before the selection overlay appears; drag a distinctive region. The overlay absorbs the selection so it does not click the target. The template is saved as a PNG, previewed, and initially uses its center as the click offset. Set a physical-pixel search region (or leave it blank for the virtual desktop), threshold, template DPI and timeout. **Test match** searches without injecting input and outlines a unique result in green for 1.6 seconds. Close the dialog to cancel a running test. Capture a larger distinctive area or narrow the search region if matching is ambiguous.

OpenCvSharp/OpenCV 4.13 runs locally. Matching uses normalized correlation, 128-row result strips, cancellation between strips, a threshold default of 0.94, and a 0.03 competing-match margin. Templates must be 8–1024 pixels per side, with grayscale standard deviation at least 8 and sufficient edges. Multiple separated candidates within the margin cause an ambiguity error. Low-detail templates, invalid regions, missing assets, unsupported scaling and click offsets outside the template are errors. Search/capture regions are limited to 16 million pixels and must lie inside the current virtual desktop. Threshold accepts 0.60–1.00. A high score is a visual heuristic, not proof of semantic identity.

Matching uses **exact physical-pixel scale 1**. Templates store capture DPI; mismatched search/result DPI is rejected. Automatic resize, rotation, perspective correction and arbitrary browser/app zoom adaptation are unsupported. Recapture after changing DPI, zoom, theme, font rendering or target appearance. Choose stable artwork; hover, focus, animations and changing text can make a template unsuitable. Screens hidden by other applications cannot be identified. MacroStudio's windows and overlays use Windows `WDA_EXCLUDEFROMCAPTURE`, so they are excluded from matching captures.

ImageClick waits for a unique match and clicks the configured offset using the selected mouse button. Its default failure behavior stops playback with a useful error and releases held input. **On image timeout, click action X,Y instead** is an explicit opt-in fallback; the action's coordinate mode and target apply to that fallback. Invalid assets, low-information/ambiguous matches and DPI/scaling errors always stop; fallback applies only after a search timeout. An image click is a complete down/up pair. Keep ordinary down/path/up actions for drags and held buttons.

**Visual recording** is optional. A separate background worker captures 96×64 context near the pointer about every 45 ms. The hook callback only queues metadata. A simple stationary click can use a frame completed before its input edge, no older than 250 ms and newer than the last non-movement edge. Drags and held clicks retain the original input actions. A click without fresh/distinctive pre-click context becomes an ImageClick requiring a template; playback validation stops rather than silently using coordinates. Inspect recording warnings and supply the missing template. Captured assets are under `%LOCALAPPDATA%\MacroStudio\Assets` until exported into a portable package.

Detection actions share cancellation, polling (100 ms default) and a finite timeout:

| Action | Condition |
| --- | --- |
| WaitImage | A unique template is visible. |
| WaitImageGone | The template is absent. Ambiguity is an error. |
| WaitPixel | Action X,Y matches `#RRGGBB` within the configured per-channel tolerance. |
| WaitRegionChange | The chosen fraction of pixels changes from the baseline captured when the action starts; grayscale absolute difference must exceed tolerance. |
| WaitWindow | A running window matches the action's process/title target; this does not launch or focus it. |

Detection timeouts use active execution time, excluding pauses at checkpoints. Captures and one OpenCV strip are synchronous bounded work; cancellation is checked between operations. Large captures can make observed timeouts exceed the configured duration by one remaining capture/strip operation; the deadline is checked between strips. Detection timeout values do not scale with playback speed.

## Screenshots, OCR, variables and data

Set screenshot/OCR regions with the visual settings dialog. **Screenshot** saves a PNG to the configured file and/or an image-reference variable. With no file specified it creates a persistent PNG under `%LOCALAPPDATA%\MacroStudio\Captures`. Blank output variable means file-only capture. An image variable refers to a file, not an in-memory bitmap.

**OcrText** uses Windows local OCR and stores recognized lines in a text variable. **OcrFind** finds one case-insensitive sequence of words and stores its screen center as a coordinate variable; zero or multiple occurrences are errors. Select an installed Windows OCR language, default `en-US`. There is no cloud OCR or automatic language download. OCR regions must fit `OcrEngine.MaxImageDimension` (queried from Windows). Accuracy depends on font size, contrast, language and layout; OCR phrase search is a word sequence, not a semantic locator.

Typed variables are text, finite numbers, booleans, physical coordinates (`X,Y`), or image file references. They reset at the beginning of each playback, persist across its repetitions, and are shared with nested calls. Relative image references are resolved when assigned. Use **Inspector** to review a stable snapshot while paused.

Use `${name}` in text, comments, window names/titles, image paths, pixel colors, data values, operands, file paths and OCR languages; `${point.x}` and `${point.y}` extract coordinates. Undefined variables and wrong types fail at use. Structural labels and macro-call paths remain static so they can be validated before any input.

The data editor also provides numeric/boolean **property bindings**, as JSON, for example:

```json
{"x":"${position.x}","y":"${position.y}","delayMs":"${delay}"}
```

Supported keys: `x`, `y`, `delayMs`, `durationMs`, `wheelDelta`, `virtualKey`, `scanCode`, `isDown`, `extended`, `detection.threshold`, `detection.timeoutMs`, `detection.templateDpi`, `detection.offsetX`, `detection.offsetY`, `detection.region.x`, `.y`, `.width`, `.height`, `data.row`, `data.column`, `flow.count`, `flow.retries`. Create a region before binding its members. Resolved values are validated against action limits. For booleans use JSON `true`/`false`; integer fields require integers.

| Action | Data settings |
| --- | --- |
| SetVariable | Output name, type and value. |
| Calculate | Left/right operands and `+ - * / %`; finite numbers only, zero divisors rejected. |
| Compare | Left/right operands and `== != contains > < >= <=`, stored as a boolean. Equality/contains use ordinal text; ordered comparisons parse finite numbers. |
| ClipboardRead / ClipboardWrite | Read into a text variable / write the configured value. Windows clipboard contention can cause an explicit error. |
| ReadText / WriteText | UTF-8 file, variable/value; WriteText optionally appends. |
| ReadCsv / WriteCsv | Read/write one zero-based cell. Quoted commas, doubled quotes and multiline values are supported. Writes preserve other cell values, but normalize CSV quoting and line endings. |

Text/CSV reads are limited to 16 MB; CSV rows to 100000 and columns to 1000 (zero based). Output directories must exist, except automatically created screenshot capture directories. File paths resolve against the current macro's folder, including inside calls.

## Flow control, debugging and movement

**Label** names a location. **Jump** selects a destination. **Branch** compares its data operands and jumps to its true or false label; a blank false destination continues. **RepeatStart** and **RepeatEnd** surround a bounded block (1–1000000 iterations). Repeat blocks can nest. Jumps must stay in the same lexical repeat block. Every instruction, including zero-duration jumps/repeats, yields and checks cancellation.

**CallMacro** runs a reusable JSON file once, then returns. Paths are relative to the caller's folder. Variables, speed, cancellation, debugger state and held-input cleanup belong to the outer session; the child's playback repetition/speed settings are ignored. Calls are preloaded and checked for recursion and a maximum depth of 12 before playback. The expanded program is limited to 200000 actions. Changes to nested files during a run do not alter its loaded program.

In flow settings, detection/AI timeouts can **Stop**, **Retry** a configured number of additional attempts (0–100), or **Jump** to a selected label. Other errors stop rather than silently continuing. Missing labels/assets, malformed repeat blocks, static invalid data and recursive calls are validated before input. Properties depending on runtime variables are validated after substitution. The GUI's validation phase is cancellable and blocks recording or document edits.

Set a **Breakpoint** on selected rows or in the action editor. **Step** starts paused or executes one action and pauses before the next; calls step into their child actions. **Pause / resume** continues normal playback. Inspector includes a variable grid and searchable logs (latest 10000 records). Logs contain action IDs, kinds, timeouts/errors and retries, without recording every typed value. Selection playback must contain the labels/blocks it needs; invalid fragments are rejected.

Name adjacent actions with the same **Group** to show a collapsible section without changing execution order. Choose **Recorded**, **Linear** or **Curved** mouse paths in playback options; **Path preview** renders the selected path in a scaled physical-pixel view. Curved paths use a smooth interpolation with a bounded perpendicular bend, preserve endpoints/duration and remain cancellable.

## Library, portability and launch

**Library** searches macro names and folder paths under `%LOCALAPPDATA%\MacroStudio\Library`. Create folders, add the current macro with assets, mark favorites, open/run a macro, or assign its global hotkey. Assignments persist in `library-index.json`. OS conflicts and duplicates with record/play/pause/stop shortcuts are rejected. A library run leaves the current editor document intact. Up to 200 total hotkeys may be registered.

File → **Export portable package** creates `.macrozip` containing the JSON, static referenced images, read-only text/CSV inputs and reusable macro files. References are rewritten relatively. Runtime-generated paths remain runtime dependencies. Import places the package in a new local-library folder and validates it; it never executes automatically. Absolute/escaping asset references, unsafe archive entries, symlinks, duplicates and missing references are rejected. Packages are limited to 2000 entries / 256 MB expanded, with 100 MB per file. Portable assets do not make desktop layouts, OCR languages, provider configurations, credentials or output file locations portable.

See [command-line and Task Scheduler setup](SCHEDULER.md). Schedules need an unlocked interactive user desktop for captures or input; MacroStudio does not unlock Windows. CLI runs ignore saved breakpoints and have an outer timeout.

## Optional AI

AI remains disabled until explicitly configured through **AI provider**. Local recording/playback, image matching, OCR, variables, data and flow control need no account or API key. No provider request is made when saving configuration or running a macro without AI actions.

Configure either a schema-capable **ChatCompletions** endpoint or the native **Ollama** `/api/chat` endpoint, plus the exact available model identifier and a 1–600 second timeout. HTTPS is required for cloud endpoints; HTTP is accepted on loopback. A bearer credential, when needed, is stored only in Windows Credential Manager as `MacroStudio/AI/Default`. The settings file contains no key. Blank credential fields keep the existing value; removal deletes it. Credentials are per Windows user and not exported.

**AiText** sends the configured data Value/prompt. **AiImage** sends that prompt and the PNG at its data File path, which can use an image variable from Screenshot. They run only as explicit actions and can be disabled individually. Text/image contents are sent to the selected provider, so choose the provider and capture region deliberately. The provider must support the selected model, JSON schema responses and image inputs when used. There is no automatic model selection or install/download.

Responses must be exactly `{"value":...}` with the selected text/number/boolean/coordinate type; coordinate output uses `{"value":{"x":1,"y":2}}`. Unknown fields, invalid types, oversized responses and malformed output fail validation. Returned prose remains text data. The application supplies no tool execution or conversion of returned prose into actions. Requests share cancellation and timeout handling. Limits are one million prompt characters, 10 MB image and 2 MB response. Redirects are disabled. Compatibility was tested with protocol mocks; no real paid/cloud model request was made for this delivery.

## Research and local dependencies

Behavior references: [Smart Record](https://www.macrorecorder.com/doc/create-macro/smart-recording/), [mouse actions](https://www.macrorecorder.com/doc/mouse/), [control functions](https://www.macrorecorder.com/doc/control/), [new features](https://www.macrorecorder.com/new/). These informed behavior, rather than supplying source code or assets.

Implementation references: [OpenCvSharp](https://github.com/shimat/opencvsharp), [Windows OCR](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine), [capture exclusion](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [Credential Manager](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew), [Ollama chat](https://docs.ollama.com/api/chat), [compatible structured chat output](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat).

See [verification and measured gaps](VERIFICATION-2.0.md) before relying on a new desktop/layout or provider.
