# RimWorld Agent Translation: player guide (English)

Other languages: [繁體中文](PLAYER_GUIDE.zh-TW.md) · [简体中文](PLAYER_GUIDE.zh-CN.md) · [日本語](PLAYER_GUIDE.ja.md) · [한국어](PLAYER_GUIDE.ko.md)

## What it is

The AI assistant you already use (for example Claude) translates RimWorld 1.7 mods into your language using this tool. It works from any source language into any language the game ships with.

| Support | Languages |
|---|---|
| Full | Simplified Chinese, Traditional Chinese, Japanese, Korean, English |
| Experimental (format checks only) | the other game languages: German, French, Russian, Spanish and so on |

- The tool never connects to an AI service and needs no API key. Your own assistant does the translating.
- Mods with English text are translated from English. Mods written in another language, such as a Chinese-only mod, can be translated into English or any other language.
- The result is a separate mod, "Agent Translation Pack". No original mod is changed, and deleting the pack removes every translation.
- Existing translations are kept and never overwritten. That includes the mod's own translations and translation patches.

> Privacy: the mod text being translated is sent to the provider of the AI assistant you use.

## You need

- RimWorld 1.7, started at least once.
- An AI assistant that supports MCP, for example the Claude desktop app.

## Three steps

### 1. Install the bridge mod (recommended)

Subscribe to "Agent Translation Bridge" and enable it in the mod list (any position). It:
- shows a settings page with a short how-to, the translation status and the pack language;
- checks in game whether each translation is really in effect;
- adds two quick buttons that open the settings page: a small icon in the bottom-right icon row (needs the Harmony mod) and a small tab button in the bottom bar. You can hide either one on the settings page.

### 2. Install rwat and connect it to your assistant

Download `rwat.exe` from the project's release page and keep it in a fixed folder, for example `D:\Tools\rwat\`.

To make sure the file was not tampered with, run this in PowerShell. The result must match the value in `SHA256SUMS.txt` from the release page:

```
Get-FileHash rwat.exe -Algorithm SHA256
```

**Claude desktop app**:
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

The Claude desktop app also uses this entry in its Code tab, so configure the server in one place only.

**Claude Code**: run this line.

```
claude mcp add rimworld-translation -- "D:\Tools\rwat\rwat.exe" serve
```

**Other MCP clients**: use the same setup. The command is `rwat.exe` and the argument is `serve`.

rwat finds the game folder, the Workshop folder and your mod list by itself.

### 3. Ask your assistant

By default the pack language follows the game language. Set the game language first, then close the game once so the setting is saved. Then tell your assistant, for example:

> Translate my active mods that are not in my language yet, then build the translation pack.

When it is done, restart the game and enable "Agent Translation Pack" in the mod list. It sorts itself after the translated mods.

## Settings page

Open it with a quick button, or from Options > Mod settings > Agent Translation.

**Simple mode** (default) shows only what most players need:
- a three-step how-to;
- the status: how many translations are in effect, and what to do next;
- the pack language (a menu; "Follow the game language" suits most players);
- switches for the two quick buttons.

**Advanced mode** adds:

| Setting | Default | Meaning |
|---|---|---|
| Write runtime report | on | Checks which translations are in effect |
| Reload Keyed translations without restarting | off | Experimental. Updates UI texts only; names and descriptions still need a restart |
| Translate keys that mods forgot to define | off | Some mods show raw key names such as `Some.Key.Name` even in English. When on, the assistant infers a text from the key, and each one is marked as inferred |
| Traditional mode: adopt existing Simplified translations | off | Converts existing Simplified translations and uses them directly. The conditions can be tuned in config.json |

- Advanced mode also shows the full report.
- The quick-button and in-game check switches take effect immediately.
- The pack language and the last two switches are saved to rwat's settings file. They take effect after you restart your assistant, and your assistant cannot change them for you.
- The settings file is `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\AgentTranslation\config.json`, next to the game's `Saves` and `Config`.

## FAQ

- **Translations do not show up**:
  - The game language must match the pack language.
  - "Agent Translation Pack" must be enabled.
  - The settings page shows what to do next. Advanced mode lists the mods whose translations are not in effect.
- **Changing the language**: pick it on the settings page (or change the game language). Restart your assistant, ask it to rebuild the pack, then restart the game.
- **Experimental languages**: placeholders, markup and line breaks are checked, but there are no language-specific checks. Look the result over in game.
- **Removing**: delete `!AgentTranslation_Pack` from the Mods folder. If you like, also unsubscribe from the bridge mod and delete the `AgentTranslation` folder above.
- **Quality**: translations are machine-generated and not proofread. Ask your assistant to use the translation memory or the glossary to keep terms consistent.
