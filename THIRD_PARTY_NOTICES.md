# Third-party notices

RimWorld Agent Translation is MIT-licensed (see `LICENSE`). The release binaries include or embed the following third-party components.

| Component | Version | License | Where | Source |
|---|---|---|---|---|
| ModelContextProtocol, ModelContextProtocol.Core (C# SDK) | 2.3.1 | Apache-2.0 | `rwat.exe` | https://github.com/modelcontextprotocol/csharp-sdk |
| Microsoft.Extensions.Hosting and its Microsoft.Extensions.* dependencies, Microsoft.Extensions.AI.Abstractions, System.Diagnostics.EventLog | 10.0.x / 10.11.0 | MIT | `rwat.exe` | https://github.com/dotnet/runtime, https://github.com/dotnet/extensions |
| .NET runtime (self-contained publish) | 10.0 | MIT | `rwat.exe` | https://github.com/dotnet/runtime |
| OpenCC dictionaries (STCharacters, STPhrases, TSCharacters, TWVariants, TWPhrases) | 1.4.2 | Apache-2.0 | embedded in `rwat.exe` | https://github.com/BYVoid/OpenCC (see `src/AgentTranslation.Server/Data/opencc/NOTICE.md`) |

Exact package versions are pinned in `src/AgentTranslation.Server/packages.lock.json`.

The in-game bridge mod (`!AgentTranslation_Bridge`) contains no third-party code. It is compiled against RimWorld's `Assembly-CSharp.dll` and `UnityStubs.dll`, which are not redistributed.

Design ideas were taken from RimWorld-Auto-AI-Translation-Core (GPL-3.0); none of its code is used.

The Apache License 2.0 text is available at https://www.apache.org/licenses/LICENSE-2.0.
