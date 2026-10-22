# Release smoke checks

Release: artifacts/MacroStudio-1.0 (self-contained Windows x64).

- Release MacroStudio.dll, MacroStudio.Windows.dll and MacroStudio.Core.dll SHA-256 hashes match the current compiled outputs.
- Updated README, format/audit documentation and three sample macros are included.
- Production services passed 17 core, seven editor and nine native integration checks. Native results and actual capture: work/native-verification/.
- Final packaged UI inspection passed: dark theme has consistent backgrounds, readable coordinate picker and toolbar; light theme renders correctly. Right-hand properties were inspected in the prior candidate.
- Packaged UI opened samples/recorded-lab.json (26 steps), launched its bundled lab, and replayed through the main Play button. The lab showed text "A", counter 2, scroll offset 48, and "Drag target reached". Main app returned to Idle, repetition 1, with "Playback complete".
- Changing speed from 1 to 2 immediately displayed the unsaved-change marker. Undo restored speed 1 and the saved baseline.
- Lab and main app closed cleanly; Get-Process confirmed no MacroStudio processes remained.
- Archive integrity evidence is generated in work/package-verification.json by scripts/verify-package.py. Every release/source archive member is checked against its source bytes, with CRC and SHA-256 verification.

Hardware environments not exercised are listed in docs/VERIFICATION.md.
