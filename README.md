# RimWorld Agent Translation

Lets any MCP-capable AI agent produce Chinese translation packs for RimWorld 1.7 mods, in Simplified Chinese (default) or Traditional Chinese (Taiwan usage).

This tool never calls an AI service and needs no API key. The agent you already use does the translating; this tool scans mods, extracts text, supplies context, translation memory and glossary, validates results and writes a standard translation pack. Original mods are never modified.

> Privacy: the mod text being translated is sent to the provider of the agent you choose.

## What it does

- Finds untranslated text (Keyed and DefInjected, with Defs inheritance resolved). Existing translations for the target language, including those from translation patch mods, count as translated and are never overwritten.
- Gives the agent memory with every batch: official game translations and other mods' existing translations of the same English, so wording stays consistent with what you already have.
- Validates every submission: placeholders, markup and newlines must match; characters of the other Chinese script are rejected; empty, untranslated and oddly sized results are flagged.
- Writes a separate pack mod (`!AgentTranslation_Pack`) that loads after the translated mods. Deleting it removes every translation.
- Traditional mode can also reuse existing Simplified translations converted with OpenCC (Apache-2.0 dictionaries, embedded).

## Quick start

1. Optional but recommended: enable the `Agent Translation Bridge` mod (any position).
2. Connect `rwat` to your MCP client (see below). It finds the game, the Workshop folder and your mod list by itself.
3. Set the game language to Simplified or Traditional Chinese, then ask your agent to translate your active mods and build the pack. Restart the game and enable `Agent Translation Pack`.

No configuration file needs editing for this. Player guides: [English](docs/guide/PLAYER_GUIDE.en.md) · [简体中文](docs/guide/PLAYER_GUIDE.zh-CN.md) · [繁體中文](docs/guide/PLAYER_GUIDE.zh-TW.md) · [日本語](docs/guide/PLAYER_GUIDE.ja.md) · [한국어](docs/guide/PLAYER_GUIDE.ko.md).

Requirements: RimWorld 1.7 started at least once (rwat keeps its work folder inside the game's data folder), Windows x64, and an MCP client.

## Release files and verification

A release contains `rwat.exe` (self-contained, no .NET install needed), `AgentTranslation_Bridge-<version>.zip`, this README, `LICENSE`, `THIRD_PARTY_NOTICES.md` and `SHA256SUMS.txt`. Check a download with:

```
Get-FileHash rwat.exe -Algorithm SHA256
```

## Build

```bash
dotnet test tests/AgentTranslation.Tests
```

```bash
powershell -ExecutionPolicy Bypass -File tools/build-release.ps1 -RimWorldDir "C:\path\to\RimWorld"
```

The release script runs the tests, publishes `rwat.exe`, builds and zips the bridge mod against your local game install (its assemblies are referenced, never copied) and writes `SHA256SUMS.txt`.

## Use with an MCP client

The server speaks MCP over stdio: `rwat serve`.

Claude Code:

```bash
claude mcp add rimworld-translation -- "C:\path\to\rwat.exe" serve
```

Claude desktop app (chat), in Settings > Developer > Edit Config:

```json
{ "mcpServers": { "rimworld-translation": { "command": "C:\\path\\to\\rwat.exe", "args": ["serve"] } } }
```

Configure the server in one place only. The Claude desktop app's Code tab also loads the servers from the chat config, and when a project `.mcp.json` uses the same name the desktop entry wins, so its `RWAT_*` environment variables apply. `get_environment` shows the folders actually in use.

Tools: `get_environment`, `list_mods`, `scan_mod`, `get_batch`, `submit_translations`, `validate_pack`, `build_pack`, `query_memory`, `glossary_get`, `glossary_add`, `adopt_simplified`, `request_reload`. None of them runs commands, reads arbitrary files or accesses the network.

## Optional in-game bridge

`Agent Translation Bridge` is a small companion mod. After the game loads it checks which pack translations are actually in effect (and which ones another mod overrides), shows the result under Options > Mod settings, and can reload Keyed texts without restarting (experimental, off by default). Its settings page also holds your rwat switches (pack language, keys used in code, auto-adopt), so players never need to open `config.json`. It only uses the `AgentTranslation` folder inside RimWorld's data folder (next to `Saves` and `Config`) and opens no network port.

```bash
dotnet build src/AgentTranslation.Bridge -p:RimWorldDir="C:\path\to\RimWorld"
```

```bash
rwat install-bridge --from artifacts/bridge-mod/!AgentTranslation_Bridge
```

## Strings in mod code

`rwat code-strings --out report.md` lists, per mod, keys that the code passes to `Translate()` but no language file defines, and English text hardcoded in UI calls. Assemblies are read as files; mod code is never loaded or run. Hardcoded text is only reported, never replaced.

## Offline mode

```bash
rwat export <packageId> --out batch.json
```

```bash
rwat import batch-result.json
```

```bash
rwat build
```

## Configuration

`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\AgentTranslation\config.json`, inside RimWorld's data folder next to `Saves` and `Config` (move the work folder with `RWAT_WORK_DIR`). Older versions used `%LOCALAPPDATA%\RimWorldAgentTranslation`; its content is copied over on first start. The game must have been started once so that its data folder exists. Main settings:

| Setting | Default | Meaning |
|---|---|---|
| `target_language` | `auto` | `auto` follows the game language (Simplified when the game is not in Chinese); or `ChineseSimplified` / `ChineseTraditional` |
| `pack_output_dir` | game `Mods` folder | Where the pack folder is written (`RWAT_PACK_OUTPUT_DIR` overrides) |
| `simplified_auto_adopt.enabled` | `false` | Lets existing Simplified translations be adopted automatically under the conditions you set |
| `code_keys.enabled` | `false` | Also offers keys that mod code uses but no language file defines (the game shows the raw key). They have no English source, so the agent infers the text; each one is flagged for review |

Only you change these settings, in this file or on the bridge mod's settings page; MCP tools cannot change them. Changes take effect when rwat restarts.

Why the game's data folder: when rwat is started by a packaged app such as the Claude desktop app, folders it creates under `AppData\Local` or `AppData\Roaming` are redirected to that app's private storage, where neither you nor the game can see them. `get_environment` warns if a data folder is set there.

## Security

See `docs/SECURITY_REVIEW.md` (Traditional Chinese) for the review against the project's security requirements. `tools/security_check.py` runs hostile inputs against `rwat serve` using scratch folders only. Mod text is treated as untrusted data; the tools never run commands, read arbitrary files or access the network, and every write goes through a path sandbox.

## License

MIT. Third-party components are listed in `THIRD_PARTY_NOTICES.md` (the MCP C# SDK and OpenCC dictionaries are Apache-2.0, .NET components are MIT). Design ideas from RimWorld-Auto-AI-Translation-Core; none of its code is used.
