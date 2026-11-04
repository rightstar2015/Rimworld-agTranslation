# RimWorld Agent Translation 玩家指南（简体中文）

其他语言：[繁體中文](PLAYER_GUIDE.zh-TW.md) ・ [English](PLAYER_GUIDE.en.md) ・ [日本語](PLAYER_GUIDE.ja.md) ・ [한국어](PLAYER_GUIDE.ko.md)

## 这是什么

让你已经在用的 AI 助手（例如 Claude）帮你把 RimWorld 1.7 的模组翻译成**简体中文或繁体中文**。

- 工具本身不连接任何 AI 服务，也不需要 API 密钥。翻译由你自己的 AI 助手完成。
- 翻译结果是一个独立的模组「Agent Translation Pack」，不会修改任何原模组。删掉它，翻译就完全移除。
- 已经有人翻译好的部分（模组自带的中文、汉化补丁）会保留，不会被覆盖。

> 隐私：需要翻译的模组文本会发送给你所使用的 AI 助手的服务商。

## 准备

- RimWorld 1.7，并且至少启动过一次游戏。
- 支持 MCP 的 AI 助手，例如 Claude 桌面版。

## 三个步骤

### 1. 安装桥接模组（推荐）

订阅「Agent Translation Bridge」，在模组列表中启用，位置不限。它会：
- 在游戏里确认每一条译文是否真的生效。
- 提供翻译工具的设置页：「选项 → 模组设置 → Agent Translation」。

### 2. 安装 rwat，连接你的 AI 助手

从项目的发布页下载 `rwat.exe`，放在固定的文件夹，例如 `D:\Tools\rwat\`。

可以用发布页上的 `SHA256SUMS.txt` 核对文件是否被篡改。在 PowerShell 运行：

```
Get-FileHash rwat.exe -Algorithm SHA256
```

算出的值应与 `SHA256SUMS.txt` 中的一致。

**Claude 桌面版（对话）**：
1. 打开「设置 → 开发者 → 编辑配置」。
2. 在配置文件中加入下面的内容，把 `command` 改成你放 rwat.exe 的位置。
3. 保存后重新启动 Claude。

```json
{
  "mcpServers": {
    "rimworld-translation": { "command": "D:\\Tools\\rwat\\rwat.exe", "args": ["serve"] }
  }
}
```

**Claude Code**：运行下面这行。

```
claude mcp add rimworld-translation -- "D:\Tools\rwat\rwat.exe" serve
```

**其他支持 MCP 的助手**：设置方式相同，命令是 `rwat.exe`，参数是 `serve`。

rwat 会自动找到游戏文件夹、创意工坊文件夹和你的模组列表。

### 3. 请助手翻译

在游戏里把语言设为简体中文或繁体中文，然后关闭游戏一次，让设置保存。翻译包的语言默认跟随游戏语言。

然后对助手说，例如：

> 帮我把已启用、还没有中文的模组翻译好，最后构建翻译包。

完成后重新启动游戏，在模组列表中启用「Agent Translation Pack」。它会自动排在被翻译的模组之后。

## 设置（都在游戏里）

打开「选项 → 模组设置 → Agent Translation」：

| 设置 | 默认 | 说明 |
|---|---|---|
| 生成运行时报告 | 开 | 检查哪些译文实际生效，结果显示在同一页 |
| 不重启游戏重新加载界面文本 | 关 | 实验性。只更新界面文本（Keyed）；物品名称与描述仍需重启游戏 |
| 翻译包语言 | 跟随游戏语言 | 也可以固定为简体或繁体中文 |
| 补上模组漏定义的键 | 关 | 有些模组的界面连英文都显示成 `Some.Key.Name`。开启后助手会根据键名推测译文，每条都会标记为推测 |
| 繁体模式：自动采用现成的简体译文 | 关 | 把现成的简体译文转换为繁体后直接使用（条件可在 config.json 中调整） |

- 后三项会保存到 rwat 的配置文件，重新启动 AI 助手后生效。
- AI 助手无法替你打开这些选项。
- 高级用户也可以直接编辑配置文件：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\AgentTranslation\config.json`，与游戏的 `Saves`、`Config` 在同一层。

## 常见问题

- **翻译没有出现**：
  - 确认游戏语言与翻译包的语言相同。
  - 确认「Agent Translation Pack」已启用。
  - 桥接模组的设置页会列出哪些模组的译文没有生效，以及被谁覆盖。
- **想换成另一种中文**：在游戏里切换语言，重新启动 AI 助手，再请它重新构建翻译包。
- **移除**：删除 Mods 文件夹中的 `!AgentTranslation_Pack`。需要的话，也取消订阅桥接模组，并删除上面那个 `AgentTranslation` 文件夹。
- **译文质量**：译文由 AI 生成，未经人工校对。需要时可以请助手用「翻译记忆」或「术语表」统一用词。
