# Valheim Console Capture publish test matrix

Manual checks before a `v*` tag. Everyday edits stop at `.\build.ps1` and ScriptEngine deploy.
Procedures stay the same across versions. Copy this section forward for a new version and reset results to `pending`.

## How to run

1. `.\build.ps1` then `..\tools\dev.ps1 deploy` (ScriptEngine). F9 reload after each fix.
2. Work the checks below in order. Use ValheimMCP for log/console/render rows while you play.
3. Logs: `C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\LogOutput.log` and `ValheimConsole.log`.
4. After ScriptEngine passes: `..\tools\dev.ps1 deploy -Plugins`, restart Valheim, re-check S01–S03.
5. Mark results `pass` / `fail`. Tag only when every result under the version heading is `pass`.

## Procedures

### S01 — BepInEx load line
**Rule:** Plugin loads for this version.
1. Deploy and enter a world.
2. Check logs for `ValheimConsoleCapture`.
3. **Pass:** load line for this version.
4. **Fail:** missing load or immediate exception.

### S02 — No exception in Gale logs
**Rule:** Clean log for this mod after the session.
1. Search Gale logs for ValheimConsoleCapture / exceptions.
2. **Pass:** no exception from this mod.
3. **Fail:** any exception from this mod.

### S03 — Config applies
**Rule:** BepInEx config still drives behavior (LogToFile, path, etc.).
1. Toggle `File.LogToFile` or related settings.
2. **Pass:** file logging follows the setting.
3. **Fail:** no effect.

### R01 — Patch only AddString(string)
**Rule:** Patch only Terminal.AddString(string); do not patch Chat; do not change displayed text or skip original.
1. Print console lines (commands / system messages). Confirm on-screen Terminal text is unchanged.
2. **Pass:** lines appear normally in F5; capture gets the same text; Chat is not patched.
3. **Fail:** Terminal text altered/suppressed, or Chat is hooked.

### R02 — No double-log of entered commands
**Rule:** Entered commands are already echoed by Terminal.InputText; do not log them a second time.
1. Type `pos` (or similar) in the console.
2. Inspect `ValheimConsole.log`.
3. **Pass:** the command appears once as echoed by vanilla, not duplicated by the mod.
4. **Fail:** duplicate command lines from the mod.

### R03 — Tab suggestions are not console lines
**Rule:** Tab suggestions write Terminal.m_search; that postfix is autocomplete, not a console line.
1. Start typing a command and use Tab autocomplete without submitting.
2. **Pass:** suggestions do not append as captured console lines.
3. **Fail:** autocomplete text is written into ValheimConsole.log as lines.

### R04 — Vanilla ConsoleCommand registration
**Rule:** Commands are vanilla Terminal.ConsoleCommand so ScriptEngine reload overwrites the delegate.
1. Deploy via ScriptEngine. Run a capture command. F9 reload. Run again.
2. **Pass:** commands still work after F9; no stacked duplicate handlers required via Jötunn CommandManager.
3. **Fail:** commands dead after reload, or registered only through Jötunn CommandManager.

### R05 — dumpcommand output
**Rule:** dumpcommand writes only command output lines to ConsoleCaptures; quoted semicolons stay one chain.
1. Run `dumpcommand pos` (or a safe read-only command).
2. Run `dumpcommand "pos; seed"` if Server Devcommands semicolon splitting is relevant.
3. **Pass:** capture file has command output only (no dump acknowledgement); quoted multi-command chain works as one dumpcommand.
4. **Fail:** acknowledgement in file, or quoted chain broken into separate unintended commands.

### R06 — UnpatchSelf on destroy
**Rule:** OnDestroy calls Harmony.UnpatchSelf(); never UnpatchAll null.
1. F9 reload twice while watching logs.
2. **Pass:** no stacked patch symptoms; no UnpatchAll warnings/errors from this mod.
3. **Fail:** duplicate capture lines after reload from stacked patches.

### R07 — Clipboard without WinForms
**Rule:** Clipboard uses GUIUtility.systemCopyBuffer.
1. Use the copy command for recent lines.
2. **Pass:** clipboard receives text; no Windows Forms dependency crash.
3. **Fail:** copy fails or WinForms exception.

### R08 — Local only
**Rule:** Local files only; no RPCs/uploads/telemetry; client-only NotEnforced.
1. Confirm output only under BepInEx paths on this machine.
2. **Pass:** no network upload behavior; works as client-only.
3. **Fail:** remote send / forced EveryoneMustHaveMod networking.

## 1.0.0

| ID | Check | Result | Notes |
| --- | --- | --- | --- |
| S01 | BepInEx load line | pass | ScriptEngine + cold plugins: BepInEx Loading [ValheimConsoleCapture 1.0.0]; log: ValheimConsoleCapture 1.0.0 loaded |
| S02 | No exception in Gale logs | pass | No ConsoleCapture/Harmony exceptions after ScriptEngine or plugins cold load |
| S03 | Config applies | pass | ScriptEngine + cold plugins: `consolecapture off/on` stops/restores log growth; TimestampLines stamps ValheimConsole.log |
| R01 | Patch only AddString(string) | pass | Postfix only on Terminal.AddString(string) for Console; Chat unpatched; F5 outputs unchanged via MCP |
| R02 | No double-log of entered commands | pass | ValheimConsole.log shows output once; no duplicate bare command echoes from the mod |
| R03 | Tab suggestions are not console lines | pass | After F9 with CaptureAutocomplete=false, Tab without Enter left no suggestion text in ValheimConsole.log |
| R04 | Vanilla ConsoleCommand registration | pass | Two F9 reloads; dumpcommand/copyconsole/consolecapture still work; ScriptEngine reload lines present |
| R05 | dumpcommand output | pass | `dumpcommand pos` and `dumpcommand "pos; seed"` wrote output-only files under ConsoleCaptures (no dump ack) |
| R06 | UnpatchSelf on destroy | pass | F9×2: UnpatchSelf path, clean reload loads, no UnpatchAll warnings; pos not multi-printed |
| R07 | Clipboard without WinForms | pass | `copyconsole 10` → Copied 8 lines; GUIUtility.systemCopyBuffer; no WinForms error |
| R08 | Local only | pass | NotEnforced; dumps/log only under local BepInEx; no RPC/upload code |
