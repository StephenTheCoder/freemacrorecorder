# Command-line and Windows Task Scheduler

Extract the whole runnable package to a permanent folder first. Paths are examples:

```powershell
& 'C:\Tools\MacroStudio\MacroStudio.exe' --help
& 'C:\Tools\MacroStudio\MacroStudio.exe' --run 'C:\Macros\daily.json' --validate --log 'C:\Macros\validation.log'
$run = Start-Process 'C:\Tools\MacroStudio\MacroStudio.exe' -ArgumentList '--run "C:\Macros\daily.json" --timeout 600 --log "C:\Macros\daily.log"' -PassThru -Wait
$run.ExitCode
```

`--validate` resolves labels, repeat blocks, image assets and nested macro calls without injecting input or requiring an interactive desktop. Runtime variables, visual identification and provider availability are checked at execution. Relative paths resolve against each macro's own directory. Calls run once using the caller's speed and share its typed variables; a child document's repetition settings are ignored.

Runs default to a one-hour outer timeout; `--timeout` accepts 1–86400 seconds. Escape or Ctrl+C cancels and releases held input. Exit codes: **0** success, **2** invalid arguments/document/missing files, **3** execution failure, **4** cancellation, **5** unavailable desktop. A log is always saved, defaulting to `%LOCALAPPDATA%\MacroStudio\Logs`. Only one recorder/CLI instance should use the global controls at a time; an occupied Escape shortcut is an explicit error.

In **Task Scheduler → Create Task**:

1. Select the Windows user that owns the macros and any provider credentials.
2. Select **Run only when user is logged on**. Keep that session **unlocked**, with the target applications open and available on the interactive desktop. Do not choose a service account or “Run whether user is logged on or not”.
3. Add the desired date/time trigger.
4. Set **Program/script** to the extracted `MacroStudio.exe`. Set **Add arguments** to `--run "C:\Macros\daily.json" --timeout 600 --log "C:\Macros\daily.log"`. Set **Start in** to `C:\Tools\MacroStudio`.
5. Under Settings, choose **Do not start a new instance** if the task is already running. Match the target's privilege level. Test with the session unlocked before relying on the schedule.

Scheduling starts the program; it cannot unlock Windows or automate a secure desktop. Captures, OCR, window focus, mouse and keyboard input need a visible interactive desktop. The CLI checks the input desktop name before injection and reports an unavailable desktop; Windows focus restrictions, other applications covering targets, monitor/DPI changes, disconnected remote sessions and target elevation can still cause a run to fail. This integration is documented; no scheduled task is installed automatically.
