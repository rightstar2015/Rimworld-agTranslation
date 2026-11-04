# RimWorld Agent Translation (work in progress)

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

No configuration file needs editing for this. A player guide draft (Traditional Chinese) is in `docs/PLAYER_GUIDE.md`.

## Build

```bash
dotnet test
```

```bash
dotnet publish src/AgentTranslation.Server -c Release
```

## Use with an MCP client

The server speaks MCP over stdio: `rwat serve`. Example for Claude Code:

```bash
claude mcp add rimworld-translation -- "C:\path\to\rwat.exe" serve
```

Tools: `get_environment`, `list_mods`, `scan_mod`, `get_batch`, `submit_translations`, `validate_pack`, `build_pack`, `query_memory`, `glossary_get`, `glossary_add`, `adopt_simplified`, `request_reload`. None of them runs commands, reads arbitrary files or accesses the network.

## Optional in-game bridge

`Agent Translation Bridge` is a small companion mod. After the game loads it checks which pack translations are actually in effect (and which ones another mod overrides), shows the result under Options > Mod settings, and can reload Keyed texts without restarting (experimental, off by default). It only uses the `AgentTranslation\bridge` folder inside RimWorld's data folder (next to `Saves` and `Config`) and opens no network port.

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

Only you edit this file; MCP tools cannot change it.

## License

MIT. Design ideas from RimWorld-Auto-AI-Translation-Core; none of its code is used. OpenCC dictionary data is Apache-2.0 (see `src/AgentTranslation.Server/Data/opencc/NOTICE.md`).
