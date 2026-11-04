# 簡轉繁抽查報告

> 產生時間：2026-11-04 09:47。遊戲 1.7.4998 rev1299。全部為**建議**，尚未寫入任何譯文。

## 摘要

| 項目 | 數值 |
|---|---|
| 抽查的模組（有簡中、無繁中） | 40 |
| 這些模組的待翻譯條目 | 14347 |
| 找得到簡中的條目 | 10535 |
| 直接採用 | 7279 |
| 交給 Agent 校對 | 2525 |
| 重新翻譯 | 731 |
| 其中含一對多字（OpenCC 有其他候選） | 1940 |
| 簡中記憶池：提供簡中的模組／條目 | 458 / 274282 |
| 官方術語：同字異體修正（用於轉換）／其中官方有多種寫法 | 94 / 1 |
| 官方用詞不同（只在整段相同時提示，不替換） | 3499 |

### 最常見的一對多字（依出現條目數）

| 字 | 條目數 |
|---|---|
| 个→個（另有 箇） | 287 |
| 了→了（另有 瞭） | 275 |
| 并→並（另有 併/幷） | 222 |
| 后→後（另有 后） | 219 |
| 它→它（另有 牠） | 162 |
| 果→果（另有 菓） | 161 |
| 具→具（另有 俱） | 134 |
| 家→家（另有 傢） | 94 |
| 象→象（另有 像） | 84 |
| 僵→僵（另有 殭） | 75 |
| 枪→槍（另有 鎗） | 74 |
| 毁→毀（另有 燬/譭） | 63 |
| 当→當（另有 噹） | 61 |
| 背→背（另有 揹） | 45 |
| 向→向（另有 嚮/曏） | 43 |

## 抽查樣本（20 筆）

### 1. 直接採用 — Ancot.Alert_MechAutoFight

- 模組：Ancot Library（keyed）
- 英文：`mechnoid auto combat`
- 簡中：`机械体正在自主作战`　來源：模組自帶，Content，有 EN 註解
- 轉換：`機械體正在自主作戰`
- 可信度：90　新鮮度：已確認
- 訊號：EN 註解吻合；模組自帶；來源覆蓋率高（99 %）

### 2. 直接採用 — ClApparelMissingBodyTexture

- 模組：ChezhouLib（keyed）
- 英文：`Cannot wear {0}: no apparel texture was found for the {1} body type. Resolved path: {2}`
- 簡中：`无法穿戴“{0}”：未找到适用于“{1}”体型的服装贴图。最终路径：{2}`　來源：模組自帶，1.6
- 轉換：`無法穿戴“{0}”：未找到適用於“{1}”體型的服裝貼圖。最終路徑：{2}`
- 可信度：80　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率低（6 %）

### 3. 直接採用 — CQF_TestDomainExpansion

- 模組：Custom Quest Framework（keyed）
- 英文：`CQF_TestDomainExpansion`
- 簡中：`展开基础领域`　來源：模組自帶，1.7/Optional/Domain
- 轉換：`展開基礎領域`
- 可信度：90　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（99 %）

### 4. 直接採用 — UniversalRaceAndXenoFix_ConvertToHumanXenotype_Label

- 模組：[HAR]Universal Race and Xeno Fix - 通用种族异种基因修复（keyed）
- 英文：`Convert to human-legal xenotype (sub-option)`
- 簡中：`转为清除异种保留人类（子选项）`　來源：模組自帶，根目錄
- 轉換：`轉為清除異種保留人類（子選項）`
- 可信度：90　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（100 %）

### 5. 直接採用 — MinditorMod.Settings.AllowManhunterOnTameFail.Label

- 模組：[MAP]监护机 Minditor（keyed）
- 英文：`Animal may become manhunter on tame fail`
- 簡中：`驯服失败时动物概率狂暴`　來源：模組自帶，Minditor
- 轉換：`馴服失敗時動物機率狂暴`
- 可信度：90　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（99 %）

### 6. 直接採用 — CollapseOneLevel

- 模組：[UR] Better Filter for Storage, Clothes and Food Restrictions（keyed）
- 英文：`Collapse one level`
- 簡中：`收起一级`　來源：模組自帶，根目錄
- 轉換：`收起一級`
- 可信度：90　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（100 %）

### 7. 直接採用 — AMO_AmortizedVisibility

- 模組：Architect Menu Optimizer（keyed）
- 英文：`Amortized visibility checks`
- 簡中：`分帧可见性检查`　來源：模組自帶，根目錄
- 轉換：`分幀可見性檢查`
- 可信度：90　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（100 %）

### 8. 直接採用 — AutoBloodDrawerConnectedBeds

- 模組：Auto Blood Drawer（keyed）
- 英文：`Connected beds`
- 簡中：`已连接床铺`　來源：模組自帶，根目錄
- 轉換：`已連接床鋪`
- 可信度：90　新鮮度：已確認
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（100 %）；套用官方術語：连接→連接

### 9. 交給 Agent 校對 — Ancot.Alert_MechAutoFightDesc

- 模組：Ancot Library（keyed）
- 英文：`They prioritize looking for threats on the map and trying to fight with them. Draft them to gain control.`
- 簡中：`它们会优先在地图内寻找威胁并试图将其消灭。征召以获得控制权。`　來源：模組自帶，Content，有 EN 註解
- 轉換：`它們會優先在地圖內尋找威脅並試圖將其消滅。徵召以獲得控制權。`
- 可信度：90　新鮮度：已確認
- 原因：一對多字：它→它（另有 牠）、并→並（另有 併/幷）
- 訊號：EN 註解吻合；模組自帶；來源覆蓋率高（99 %）

### 10. 交給 Agent 校對 — Ancot.Turret

- 模組：Ancot Library（keyed）
- 英文：`turret`
- 簡中：`炮塔`　來源：模組自帶，Content，有 EN 註解
- 轉換：`炮塔`
- 可信度：90　新鮮度：已確認
- 原因：官方繁中對同一簡中詞譯為「砲塔」（用詞不同，非逐字轉換）
- 訊號：EN 註解吻合；模組自帶；來源覆蓋率高（99 %）

### 11. 交給 Agent 校對 — Ancot.LowVersionWarning

- 模組：Ancot Library（keyed）
- 英文：`Ancot Library version is too low`
- 簡中：`Ancot Library版本过低`　來源：模組自帶，Content，有 EN 註解
- 轉換：`Ancot Library版本過低`
- 可信度：75　新鮮度：已確認
- 原因：簡中殘留英文：Ancot、Library
- 訊號：EN 註解吻合；模組自帶；來源覆蓋率高（99 %）

### 12. 交給 Agent 校對 — Milira.MilianTab_Button

- 模組：Milira Race（keyed）
- 英文：`button`
- 簡中：`按钮`　來源：模組自帶，1.5（未載入）
- 轉換：`按鈕`
- 可信度：70　新鮮度：已確認
- 原因：官方繁中對同一簡中詞譯為「鈕扣」（用詞不同，非逐字轉換）；來源為舊版本資料夾
- 訊號：同資料夾英文吻合；模組自帶；來自未載入的資料夾 1.5

### 13. 交給 Agent 校對 — Ancot.NerverMind

- 模組：Ancot Library（keyed）
- 英文：`nerver mind`
- 簡中：`不用了`　來源：模組自帶，Content，有 EN 註解
- 轉換：`不用了`
- 可信度：90　新鮮度：已確認
- 原因：一對多字：了→了（另有 瞭）
- 訊號：EN 註解吻合；模組自帶；來源覆蓋率高（99 %）

### 14. 交給 Agent 校對 — CL_MapEditingClosing

- 模組：ChezhouLib（keyed）
- 英文：`正在关闭CL模板编辑地图`
- 簡中：`正在关闭CL模板编辑地图`　來源：模組自帶，1.6
- 轉換：`正在關閉CL模板編輯地圖`
- 可信度：40　新鮮度：已確認
- 原因：簡中與英文相同（未翻譯）
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率低（6 %）

### 15. 交給 Agent 校對 — CQF_TestDomainBusy

- 模組：Custom Quest Framework（keyed）
- 英文：`CQF_TestDomainBusy`
- 簡中：`CQF_TestDomainBusy`　來源：模組自帶，1.7/Optional/Domain
- 轉換：`CQF_TestDomainBusy`
- 可信度：50　新鮮度：已確認
- 原因：簡中與英文相同（未翻譯）
- 訊號：同資料夾英文吻合；模組自帶；來源覆蓋率高（99 %）

### 16. 重新翻譯 — Ancot_KnockbackResistance.description

- 模組：Ancot Library（def / StatDef）
- 英文：`This creature's resistance to Knockback.`
- 簡中：`此生物对于击退的抵抗能力。`　來源：模組自帶，Content，有 EN 註解
- 轉換：`此生物對於擊退的抵抗能力。`
- 可信度：75　新鮮度：過期
- 原因：簡中檔的 EN 註解與目前英文不同；一對多字：抵→抵（另有 牴）
- 訊號：EN 註解不符；模組自帶；來源覆蓋率高（99 %）

### 17. 重新翻譯 — CQFActionCategory_DataWrite

- 模組：Custom Quest Framework（keyed）
- 英文：`Data write`
- 簡中：`数据写用`　來源：模組自帶，根目錄，有 EN 註解
- 轉換：`資料寫用`
- 可信度：75　新鮮度：過期
- 原因：簡中檔的 EN 註解與目前英文不同
- 訊號：EN 註解不符；模組自帶；來源覆蓋率高（99 %）

### 18. 重新翻譯 — MinditorInterrogateIdentity.description

- 模組：[MAP]监护机 Minditor（def / JobDef）
- 英文：`Interrogates a prisoner's identity to detect metalformers`
- 簡中：`审问身份。`　來源：模組自帶，Anomaly
- 轉換：`審問身份。`
- 可信度：75　新鮮度：過期
- 原因：簡中所在資料夾的英文與目前英文不同
- 訊號：同資料夾英文不符；模組自帶；來源覆蓋率高（99 %）

### 19. 重新翻譯 — RST_Transform.label

- 模組：Draconis Koelime（def / AbilityDef）
- 英文：`form shift (Dragon Form)`
- 簡中：`形态切换（龙形态）`　來源：模組自帶，根目錄，有 EN 註解
- 轉換：`形態切換（龍形態）`
- 可信度：70　新鮮度：過期
- 原因：簡中檔的 EN 註解與目前英文不同
- 訊號：EN 註解不符；模組自帶

### 20. 重新翻譯 — Milira_ArtemisArmor.label

- 模組：Milira Race（def / ThingDef）
- 英文：`milira Artemis Armor (Assault)`
- 簡中：`米莉拉阿忒弥斯战甲（突击）`　來源：模組自帶，1.6，有 EN 註解
- 轉換：`米莉拉阿忒彌斯戰甲（突擊）`
- 可信度：70　新鮮度：過期
- 原因：簡中檔的 EN 註解與目前英文不同；一對多字：弥→彌（另有 瀰）
- 訊號：EN 註解不符；模組自帶

## 抽查的模組

| 模組 | 待翻譯 | 有簡中 |
|---|---|---|
| Ancot Library | 364 | 361 |
| ChezhouLib | 69 | 4 |
| Custom Quest Framework | 3263 | 3246 |
| [HAR]Universal Race and Xeno Fix - 通用种族异种基因修复 | 24 | 24 |
| [MAP]监护机 Minditor | 139 | 137 |
| [O21] WikiRim (Continued) | 0 | 0 |
| [UR] Better Filter for Storage, Clothes and Food Restrictions | 142 | 142 |
| Architect Menu Optimizer | 44 | 44 |
| Auto Blood Drawer | 36 | 36 |
| Auto Cutter | 24 | 24 |
| AutoUpgrade | 21 | 21 |
| Beat Your Prisoners (Continued) | 120 | 39 |
| Better Workbench Management | 67 | 61 |
| Capture Them (Continued) | 11 | 11 |
| Designator Shapes | 208 | 73 |
| Draconis Koelime | 222 | 122 |
| Dubs Mint Menus | 57 | 57 |
| Dubs Mint Minimap | 60 | 58 |
| Export and Import Mod Settings | 20 | 20 |
| Jump All Mech | 10 | 10 |
| Just Help For Me | 45 | 45 |
| Maiya Labeler 麦丫标签机 | 47 | 47 |
| Make Anomaly Easier:Detector For Invisibility And Metalhorror | 9 | 9 |
| Make Reinforced Barrel | 3 | 3 |
| Milira Race | 2138 | 1450 |
| Milira Race Gene Patch | 21 | 21 |
| MoeLotlNameExpansion | 0 | 0 |
| Monolyn Race | 2538 | 1855 |
| Monolyn TerminalExtension | 85 | 80 |
| Monolyn: Mecha · 墨诺灵机甲 | 198 | 180 |
| Mya's fufu | 162 | 155 |
| doctor dragonian | 73 | 49 |
| MoeLotl: Rigor Mortis | 1061 | 909 |
| [Bendan]RatkinPsycast | 1493 | 27 |
| Non uno Pinata (don't drop items) | 0 | 0 |
| OuterrealmTech 超维科技 | 1176 | 1161 |
| Pawn Education (Continued) | 345 | 2 |
| Pawn Texture Offset Adjuster | 35 | 35 |
| Priority Treatment Ressurected | 17 | 17 |
| Prisoner Arena (Continued) | 0 | 0 |
