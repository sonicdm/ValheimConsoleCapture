# AGENTS.md — Valheim Console Capture

Guidance for Cursor agents (and humans) working in this repo.

## What this is

Client-side BepInEx mod that mirrors the F5 `Terminal` into `BepInEx/ValheimConsole.log` and can copy or dump the recent lines.

| | |
| --- | --- |
| GUID | `com.sonicdm.valheim.valheimconsolecapture` |
| Assembly | `ValheimConsoleCapture.dll` |
| Config | `BepInEx/config/com.sonicdm.valheim.valheimconsolecapture.cfg` |
| Source | `src/` |

## Product rules

- Patch only `Terminal.AddString(string)`. The other overloads call it. Do not patch Chat.
- Do not change the text Valheim displays or skip the original method.
- Entered commands are already echoed by `Terminal.InputText` before `TryRunCommand`. Do not log them a second time.
- Tab suggestions are not console lines. `updateSearch` writes `Terminal.m_search`. That postfix is the autocomplete path.
- Commands are vanilla `Terminal.ConsoleCommand` so a ScriptEngine reload overwrites the delegate. Do not register them through Jötunn `CommandManager` (that list keeps the first assembly).
- `dumpcommand altbioms` runs that console command and writes only its lines to `BepInEx/ConsoleCaptures`. `dumpcommand "pos; seed"` runs each command in order into the same file. Do not include the dump acknowledgement in the file. Server Devcommands splits `TryRunCommand` on every `;` and runs those pieces on a later frame. A first-priority prefix hides semicolons inside the quoted `dumpcommand` argument so the chain arrives as one command. Each segment is shielded again before it runs, then the semicolon is restored before that command reads its arguments. Semicolons outside the wrapping quotes stay multi-command separators.
- `OnDestroy` calls `Harmony.UnpatchSelf()` so F9 reload does not stack patches. Never `UnpatchAll()` with a null id.
- Clipboard uses `GUIUtility.systemCopyBuffer`. No Windows Forms.
- Local files only. No RPCs, uploads, or telemetry.
- Client-only (`NotEnforced`). The log is still useful if someone runs it on a dedicated server; nothing is sent to other players.

## Dev test

Use the workspace tool. Do not add another `deploy-dev.ps1`.

```powershell
..\tools\dev.ps1 deploy
```

That builds and copies the DLL into the Gale Default profile `BepInEx\scripts` for ScriptEngine. Press **F9** to reload. `-Plugins` is the normal `plugins\` install, for when this mod is ready to package.

`..\tools\dev.ps1 install` and `disable` manage ScriptEngine once for the profile.

Do not run `package.ps1` / `release.ps1` unless asked.

## Version bumps

Current release is **`1.0.0`**. On the next ship, bump all of these together: `PluginVersion`, csproj `<Version>`, `manifest.json` `version_number`, the README Version row, and `CHANGELOG.md`.

## Install

Do not hand-copy the DLL into a profile. The workspace tool does that.
