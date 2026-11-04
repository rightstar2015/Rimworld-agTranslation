# RimWorld Agent Translation: player guide (English)

Other languages: [繁體中文](PLAYER_GUIDE.zh-TW.md) · [简体中文](PLAYER_GUIDE.zh-CN.md) · [日本語](PLAYER_GUIDE.ja.md) · [한국어](PLAYER_GUIDE.ko.md)

## What it is

Lets the AI assistant you already use (for example Claude) translate RimWorld 1.7 mods into **Simplified or Traditional Chinese**. Other target languages are not supported yet.

- The tool itself never connects to an AI service and needs no API key. Your own assistant does the translating.
- The result is a separate mod, "Agent Translation Pack". No original mod is changed. Delete it and every translation is gone.
- Existing translations (Chinese shipped by the mod, translation patches) are kept and never overwritten.

> Privacy: the mod text being translated is sent to the provider of the AI assistant you use.

## You need

- RimWorld 1.7, started at least once.
- An AI assistant that supports MCP, for example the Claude desktop app.

## Three steps

### 1. Install the bridge mod (recommended)

Subscribe to "Agent Translation Bridge" and enable it in the mod list, in any position. It:
- checks in game whether each translation is really in effect;
- adds the tool's settings page under Options > Mod settings > Agent Translation.

### 2. Install rwat and connect it to your assistant

Download `rwat.exe` from the project's release page and keep it in a fixed folder, for example `D:\Tools\rwat\`.

You can check that the file was not tampered with against `SHA256SUMS.txt` from the release page. In PowerShell, run:

```
Get-FileHash rwat.exe -Algorithm SHA256
```

The result must match the value in `SHA256SUMS.txt`.

**Claude desktop app (chat)**:
1. Open Settings > Developer > Edit Config.
2. Add the following, with `command` set to where you put rwat.exe.
3. Save and restart Claude.

```json
{
  "mcpServers": {
    "rimworld-translation": { "command": "D:\\Tools\\rwat\\rwat.exe", "args": ["serve"] }
  }
}
```

**Claude Code**: run this line.

```
claude mcp add rimworld-translation -- "D:\Tools\rwat\rwat.exe" serve
```

**Other MCP clients**: the same setup applies. The command is `rwat.exe` and the argument is `serve`.

rwat finds the game folder, the Workshop folder and your mod list by itself.

### 3. Ask your assistant

Set the game language to Simplified or Traditional Chinese, then close the game once so the setting is saved. By default the pack language follows the game language.

Then tell your assistant, for example:

> Translate my active mods that have no Chinese yet, then build the translation pack.

When it is done, restart the game and enable "Agent Translation Pack" in the mod list. It sorts itself after the translated mods.

## Settings (all in game)

Open Options > Mod settings > Agent Translation:

| Setting | Default | Meaning |
|---|---|---|
| Generate runtime report | on | Checks which translations are in effect; results show on the same page |
| Reload UI texts without restarting | off | Experimental. Updates UI texts (Keyed) only; item names and descriptions still need a restart |
| Pack language | follow the game | Or fixed to Simplified or Traditional Chinese |
| Translate keys that mods forgot to define | off | Some mods show raw key names such as `Some.Key.Name` even in English. When on, the assistant infers a text from the key; each one is marked as inferred |
| Traditional mode: adopt existing Simplified translations | off | Converts existing Simplified translations and uses them directly (conditions can be tuned in config.json) |

- The last three are saved to rwat's settings file and take effect after you restart your assistant.
- Your assistant cannot turn them on for you.
- Advanced users can also edit the settings file directly: `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\AgentTranslation\config.json`, next to the game's `Saves` and `Config`.

## FAQ

- **Translations do not show up**:
  - Check that the game language matches the pack language.
  - Check that "Agent Translation Pack" is enabled.
  - The bridge settings page lists the mods whose translations are not in effect, and which mod overrides them.
- **Switching to the other Chinese**: change the game language, restart your assistant, then ask it to rebuild the pack.
- **Removing**: delete `!AgentTranslation_Pack` from the Mods folder. If you like, also unsubscribe from the bridge mod and delete the `AgentTranslation` folder above.
- **Quality**: translations are machine-generated and not proofread. Ask your assistant to use the translation memory or the glossary to keep terms consistent.
