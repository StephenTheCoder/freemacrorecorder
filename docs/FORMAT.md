# MacroStudio JSON formats v1 and v2

UTF-8 JSON. The root has `formatVersion: 1` or `2`, `name`, ordered `actions`, and `playback`. Enum names are strings. No transient window handles or executable commands are serialized. Unknown fields and unsupported versions are rejected.

`playback`: `speed` (0.05–100 multiplier), `repetitions` (1–1000000), `untilStopped`, `skipMousePaths`, `restoreWindows` (booleans).

Every action has a unique `id` (UUID), `enabled`, `kind`, `delayMs` (wait before action), `durationMs`, `coordinates` (`Desktop` or `Window`), optional `target`, and `comment`. Delays/durations must be finite, 0–86400000 ms. Disabled actions and their delays are omitted. Timings scale by `1/speed`; path sample offsets are relative to path start. Pausing freezes playback time.

| Kind | Payload |
| --- | --- |
| MousePath | `path`: ordered objects `{ "x": -100, "y": 200, "offsetMs": 0 }`. Offsets must be within `durationMs`. Consecutive recorded moves form one action; timing before the first sample is in `delayMs`. |
| MouseButton | `x`, `y`, `button` (`Left`, `Right`, `Middle`), `isDown`. A down and up pair is a click; two pairs preserve a double-click. A path between down/up preserves a drag. |
| Scroll | `x`, `y`, signed `wheelDelta` (120 is one notch), `horizontal`. |
| Key | `virtualKey` (1–255), `scanCode` (0–65535), `extended`, `isDown`. Nonzero scan code uses Windows scan-code injection. |
| KeyChord | `keys`: nonempty list of Windows virtual-key codes; press in order and release in reverse. Put modifiers first. |
| Text | `text`: Unicode, sent as UTF-16 code units with KEYEVENTF_UNICODE; supports surrogate pairs. |
| Delay | Wait for `durationMs` after `delayMs`. |
| FocusWindow | Requires `target`; optionally restore bounds before activating. |

`target` is `{ "processName": "notepad", "title": "Example", "left": -1200, "top": 0, "width": 900, "height": 700 }`. Process excludes `.exe`. A window-relative pointer action requires a target; desktop pointer actions use physical virtual-desktop pixels. Relative offsets are measured from the outer window rectangle in physical pixels. The app declares PerMonitorV2 DPI awareness; playback normalizes absolute SendInput coordinates over the entire virtual desktop with MOUSEEVENTF_VIRTUALDESK.

Fields unused by an action kind are retained with default values for a simple stable schema. Strings `text` and `comment`, arrays `path` and `keys`, and document options cannot be null. Files are limited to 100 MB, documents to 200000 actions, text/path data to 1000000 entries/code units per action. Atomic saves write and flush a same-directory temporary file, then replace/move it into place. The old file survives validation and write failures before replacement.

See `samples/` for minimal hand-authored examples. Omitted known fields take model defaults; all files saved by MacroStudio include complete fields.


## Version 2 additions

New documents default to version 2. Version 1 input files retain their existing action semantics and can be opened/edited. New optional fields have defaults, so v1 files do not need migration before reading. The original v1 application cannot read the extended fields in a v2-produced file; use this version to reopen updated files.

Every action additionally accepts `detection`, `data`, `bindings`, `flow`, `breakpoint` and `group`. `playback.pathShape` is `Recorded` (default), `Linear` or `Curved`. Unused fields remain present in serialized documents. See [ADVANCED.md](ADVANCED.md) for action semantics, limits and supported property bindings.

- `detection`: `image`, optional `region` (`x`, `y`, `width`, `height` physical pixels), `threshold`, `timeoutMs`, `pollMs`, `templateDpi`, `scale` (only 1), `offsetX`, `offsetY`, `coordinateFallback`, `color`, `colorTolerance`, `changeFraction`.
- `data`: `variable`, `type` (`Text`, `Number`, `Boolean`, `Coordinates`, `Image`), `value`, `operand`, `operation`, `file`, `append`, zero-based `row` and `column`, and OCR `language`.
- `bindings`: supported property path to a substitution expression string. It is not a general expression language or code evaluator.
- `flow`: `label`, `destination`, `otherwise`, bounded `count`, static `macroFile`, timeout policy `timeout` (`Stop`, `Retry`, `Jump`) and additional `retries`.
- `breakpoint`: pause before the action in GUI playback; CLI ignores it.
- `group`: adjacent equal names form collapsible presentation sections. The serialized action order is always the execution order.

Additional kinds: `ImageClick`, `WaitImage`, `WaitImageGone`, `WaitPixel`, `WaitRegionChange`, `WaitWindow`, `Screenshot`, `OcrText`, `OcrFind`, `SetVariable`, `Calculate`, `Compare`, `ClipboardRead`, `ClipboardWrite`, `ReadText`, `WriteText`, `ReadCsv`, `WriteCsv`, `Label`, `Jump`, `Branch`, `RepeatStart`, `RepeatEnd`, `CallMacro`, `AiText`, `AiImage`.

AI provider configuration and credentials are not serialized in macros or portable packages. A portable `.macrozip` has root `macro.json`, content-addressed `assets/` and optional reusable `macros/`; paths are rewritten relatively. Runtime-generated paths remain dependencies of the executing environment. Import never runs the macro.
