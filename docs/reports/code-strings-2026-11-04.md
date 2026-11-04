# 程式碼中的字串

> 產生時間：2026-11-04 22:52。遊戲 1.7.4998 rev1299。目標語言 **ChineseSimplified**。範圍：啟用中的非官方模組。只讀取 DLL 檔案內容，不載入、不執行模組程式碼。

## 摘要

| 類型 | 模組數 | 數量 | 處理方式 |
|---|---|---|---|
| A. 程式碼有用、英文與目標語言都沒定義的鍵（遊戲顯示鍵名） | 51 | 190 | 未啟用（config.json 的 `code_keys.enabled`） |
| B. 直接寫在介面呼叫裡的英文（啟發式判斷） | 64 | 587 | 只回報；無法用語言檔翻譯，可轉告模組作者 |

限制：只找得到「字串常數直接傳給呼叫」的情況，用字串拼接或變數產生的鍵與文字找不到。B 類已排除 `DEV`／`Debug` 開頭的開發者字串與看起來像識別碼的字串，仍可能有誤判。

## A. 缺少定義的鍵（最多 40 個模組）

| 模組 | 數量 | 例子（用在哪裡） |
|---|---|---|
| Vanilla Expanded Framework | 17 | `LaserTurretNotCharged`（Building_LaserGun.GetInspectString）<br>`VWE_WeaponDeteriorationInfo`（CompApplyWeaponTraits.CompInspectStringExtra）<br>`VWE_WeaponDeterioratedMessage`（CompApplyWeaponTraits.Notify_UsedWeapon）<br>`VEF.IsToo`（StatPart_Ammo.ExplanationPart） |
| SRALib | 15 | `SRA_InterceptDropPod_LetterLabel`（GameComponent_DropPodInterceptor.SendInterceptionLetter）<br>`SRA_InterceptDropPod_LetterText`（GameComponent_DropPodInterceptor.SendInterceptionLetter）<br>`AircraftManagerNotFound`（CompAbilityEffect_AircraftStrike.Apply）<br>`CooldownAircraft`（CompAbilityEffect_AircraftStrike.ExtraLabelMouseAttachment） |
| Monolyn Race | 11 | `NoPawns`（Building_BaptismalFont.<GetGizmos>b__66_0）<br>`PhoneBoothTextC`（CompPhoneBooth.DownRandomColonist）<br>`PhoneBoothTextF`（CompPhoneBooth.FrenzyAllPawn）<br>`PhoneBoothTextB`（CompPhoneBooth.FrenzyRandomColonist） |
| Universal Blueprints | 11 | `UB_ModManagement_VersionMismatchHint`（ModManagementWindow.DrawModRow）<br>`UB_ModManagement_IgnoreVersionDifferencesHint`（ModManagementWindow.DrawModRow）<br>`UB_ModManagement_VersionMismatch`（ModManagementWindow.DrawModRow）<br>`UB_ModManagement_IgnoreVersionDifferences`（ModManagementWindow.DrawFooter） |
| 特性工作台 | 9 | `formerlyColonistInfo`（GrammarUtility+<RulesForPawn>d__1.MoveNext）<br>`discoveryMethod`（QuestPart_AddGiverQuest.GetSlate）<br>`Undefined`（TechLevelUtility.ToStringHuman）<br>`SnowAndSandClear`（Area_SnowOrSandClear.get_Label） |
| Ancot Library | 8 | `Ancot.TransporterNotAvailable`（JobDriver_HaulToOwnTransporter.HaulToTransporter）<br>`Ancot.droneAmount`（ScenPart_StartDrones.DoEditInterface）<br>`Anoct.WeaponIsNull`（WeaponTraitsUtility.CanUseTraits）<br>`Anoct.TraitIsNull`（WeaponTraitsUtility.CanAddTraits） |
| Choose Your Medicine | 8 | `Kopp_CYM_HelperWhichMedicine1`（MainTabWindow_Medicine_HelperGui.DoWindowContents）<br>`Kopp_CYM_HelperWhichMedicine2`（MainTabWindow_Medicine_HelperGui.DoWindowContents）<br>`Kopp_CYM_HelperWhichMedicine3`（MainTabWindow_Medicine_HelperGui.DoWindowContents）<br>`Kopp_CYM_HelperWhichMedicine4`（MainTabWindow_Medicine_HelperGui.DoWindowContents） |
| Milira Event Story Expand: The Tale of Milira | 8 | `LetterLabelTheTaleOfMiliraTheRuinOutpost_TechDat_m`（TaleOfMilira_TheRuinOutpost_m.Outcome_TechData）<br>`LetterLabelTheTaleOfMiliraTheRuinOutpost_TechDat`（TaleOfMilira_TheRuinOutpost.Outcome_TechData）<br>`invite_friendly`（MiliraMahouShoujoDialogue.BuildRouter）<br>`invite_hostile`（MiliraMahouShoujoDialogue.BuildRouter） |
| [OA]Ratkin Faction: Oberonia aurea | 7 | `shipTypeText`（QuestNode_ScienceShip_SetQuestData.RunInt）<br>`OARK_Reward_AssistPointTip`（Reward_AssistPoint.GetDescription）<br>`OARK_Reward_GravTechAssistPointTip`（Reward_GravTechAssistPoint.GetDescription）<br>`OARK_Reward_GravTechPointTip`（Reward_GravTechPoint.GetDescription） |
| Milira:Wings of Democracy | 5 | `PLAMilira_Church_Reward_TecPointTip`（Reward_Church_TecPoint.GetDescription）<br>`PLAMilira_Reward_ReinforcementsTip`（Reward_Reinforcements.GetDescription）<br>`PLAMilira_Reward_ThreatPointTip`（Reward_ThreatPoint.GetDescription）<br>`PLAMilira.SupportChange`（Comp_ChangeSupport+<CompGetGizmosExtra>d__32.MoveNext） |
| Raven Race - Embers in Shadow | 5 | `Occupant`（Building_Concealment.GetInspectString）<br>`Ready`（CompRavenArchonTreasure.CompInspectStringExtra）<br>`AG_EndTimeStop`（Hediff_ViskaBloodline+<GetGizmos>d__0.MoveNext）<br>`AG_EndTimeStopDesc`（Hediff_ViskaBloodline+<GetGizmos>d__0.MoveNext） |
| Milira Faction: Milira Imperium | 5 | `MiliraImperium_UnknownHostile`（MiliraImperiumSupportManager.TryExecuteImperiumGift）<br>`NoNegotiator`（Dialog_MiliraImperium_RoyalAid_Window.GetCommsFailure）<br>`MI_Mira_SkipPawn`（HediffComp_VanishAfterDelayWithLetter.TrySendLetter）<br>`Milira.RemielStorytellerLabel`（StorytellerDef_MiliraRemiel.get_OverrideLabel） |
| Ariandel Library | 4 | `AL_CannotJoinRitual_SpecialPawn`（Patch_PsychicRitualRoleDef_PawnCannotDoReason.Prefix）<br>`Pawn`（SpecialPawnManager.TryStorePawnForExternalActivity）<br>`AL_RitualCanceledObstructed`（CompShroudRitualSpot.CompTick）<br>`UnknownAbility`（AriandelLibrary_Utility_Ability.SendXpCostSuccessMessage） |
| AutoBlink | 4 | `AutoBlink17.Toggle`（BlinkCommand.Prepare）<br>`AutoBlink17.CommandDesc`（BlinkCommand.Prepare）<br>`AutoBlink17.Minimum`（BlinkSettingsWindow.DoWindowContents）<br>`AutoBlink17.Reset`（BlinkSettingsWindow.DoWindowContents） |
| Dubs Bad Hygiene | 4 | `Fertilizer`（Area_Fertilizer.get_Label）<br>`NoVacDump`（Building_SpacerToilet.Working）<br>`CannotDispenseFood`（Building_Fridge+<GetFloatMenuOptions>d__23.MoveNext）<br>`ConsumeFood`（Building_Fridge+<GetFloatMenuOptions>d__23.MoveNext） |
| Nice Health Tab | 4 | `nibato.AutoExtractGenes.AutoExtractGenes`（AutoExtractGenes_Compatibility.get_Label）<br>`nibato.AutoExtractGenes.AutoExtractGenes.Tooltip`（AutoExtractGenes_Compatibility.get_Tooltip）<br>`CE_mmRHA`（CombatExtended_Compatibility.GetCE_mm）<br>`VREA.NeutrolossRate`（BloodDisplayProfile.ForPawn） |
| Ratkin clothes维多利亚的港湾 | 4 | `FullAmmo`（CompUseEffect_ReloadGunlance.CanBeUsedBy）<br>`RK_MakeGeneratePower`（CompPowerPlantHamsterWheel.CompInspectStringExtra）<br>`AbilityPrayerServiceDisabledNoPulpit`（Command_AbilityPrayService.DisabledCheck）<br>`CanLow`（Shield+<SpecialDisplayStats>d__21.MoveNext） |
| Custom Quest Framework | 3 | `GenerateSubMap`（CustomMapEntrance.GenerateCustomMap）<br>`Colorbase`（CQFEditorTools.DrawSelectColorButtons）<br>`Hex`（CQFEditorTools.DrawSelectColorButtons） |
| NewRatkinPlus | 3 | `RK_MakeGeneratePower`（CompPowerPlantHamsterWheel.CompInspectStringExtra）<br>`AbilityPsycastNoPsyfocus`（CompAbilityEffect_EnergyShieldBoost.HasRequiredPsycastResources）<br>`CanLow`（Shield+<SpecialDisplayStats>d__3.MoveNext） |
| Numbers | 3 | `Recruitable`（PawnColumnWorker_Loyalty.GetTextFor）<br>`UnwaveringlyLoyal`（PawnColumnWorker_Loyalty.GetTextFor）<br>`UnwaveringlyLoyalDesc`（PawnColumnWorker_Loyalty.GetTip） |
| PA's God Hands | 3 | `GodHand.Assistant.TargetMustBePawnOrBed`（GodAssistantController.Execute）<br>`GodHand.Poke.ResurrectionDisabled`（PokeGodHandController.PokeCorpse）<br>`GodHand.Demo.NoPocketGenerator`（DemoReplayController.StartRecording） |
| Ratkin Misc+ 鼠族拓展:边缘杂货铺 | 3 | `TraumaTeamOnCooldown`（CompAbilityEffect_CallTraumaTeam.Apply）<br>`AbilityNotAvailable`（CompAbilityEffect_CallTraumaTeam.GizmoDisabled）<br>`CanLow`（Shield_3+<SpecialDisplayStats>d__19.MoveNext） |
| Ratkin Knights+ | 3 | `AtlarMustBePlacedInside`（RKK_RoomRoleWorker_Auditorium.Validate）<br>`Sanguophage`（RKK_Building_Atlar.TickRare）<br>`CanLow`（Shield+<SpecialDisplayStats>d__27.MoveNext） |
| Tabula Rasa | 3 | `Designator_Cancel`（Comp_SlotLoadable+<EquippedGizmos>d__26.MoveNext）<br>`Designator_CancelDesc`（Comp_SlotLoadable+<EquippedGizmos>d__26.MoveNext）<br>`TabulaRasa_CantTransformDamaged`（Comp_TransformThing+<CompGetGizmosExtra>d__2.MoveNext） |
| Wolfein Allegiance | 3 | `questName`（QuestNode_EmpireNegotiation.RunInt）<br>`questDescription`（QuestNode_EmpireNegotiation.RunInt）<br>`supplies`（QuestNode_SupplyRequest.RunInt） |
| AB's Head Apparel Tweaker | 2 | `useIsColonistCache`（TranslationOfHATSetting..ctor）<br>`Path`（SettingsWindowContents.DrawPatchSettings） |
| Kiiro Story: Events Expanded | 2 | `CommandTradeFailSocialDisabled`（KiiroMerchantCompUtility.MerchantInvestigateCommand）<br>`MessageBeggarsLeavingWithItems`（QuestNode_Root_KiiroBeggars+<>c__DisplayClass9_0.<RunInt>b__1） |
| Mad Skills+ | 2 | `VSE.ForgetSpeed`（Compatible.VSE_AddForgetRateInfo_Prefix）<br>`VFEE.Honors`（Compatible.VSE_AddForgetRateInfo_Prefix） |
| MigCorp - Skiptech | 2 | `MigCorp.Skiptech.Settings.Tuning`（MigcorpSkiptechMod.DrawSkipNetTuningSection）<br>`MigCorp.Skiptech.Settings.SkipNetTuning.Reset`（MigcorpSkiptechMod.TuningRow） |
| Milira Race | 2 | `Ancot.NoIngredientAbility`（CompAbilityCheckCarrier.GizmoDisabled）<br>`MilianCluster`（Milira_ClusterBuildingDesignate_Patch.Postfix） |
| Milira Addon: Fianchetto Variation | 2 | `FunnelBit_LaserMissingSource`（CompAbilityEffect_AreaLaser.GizmoDisabled）<br>`FunnelBit_LaserDronesUnavailable`（CompAbilityEffect_AreaLaser.GizmoDisabled） |
| [OA]Ratkin Gene Expand | 2 | `OAGene_MessageGeneDiscriminatNoPowerEjected`（Building_GeneDiscriminatorBase.Tick）<br>`OAGene_GenepackUsedByOther`（Building_GeneDiscriminatorBase.TryStartWork） |
| [OA]Ratkin Scenario: Snowstorm Orphan | 2 | `OAGene_LostInMemoryReason`（HediffComp_ProtagonistHomecomed.TryDoMentalBreak）<br>`OAGene_SnowstormStory`（ScenPart_SnowstormStory.Summary） |
| Use This Instead | 2 | `SMU.SteamMod`（Dialog_ModReplacements.DoWindowContents）<br>`SMU.LocalMod`（Dialog_ModReplacements.DoWindowContents） |
| 联合重工-UF Heavy Industries | 2 | `RequiresResearch`（Comp_FreeElectronOrbitalLaser.ResearchRequirementMet）<br>`Turbojet_MagazineEmpty`（Ability_MagazineLaunch.GizmoDisabled） |
| 萌螈流派拓展：蛊修 \| MoeLotl School Expansion: Gu Cultivation | 2 | `GuXiu_Vial_OverVial`（GuXiu_Patch_VialSwap.Exchange）<br>`GuXiu_Vial_OverBody`（GuXiu_Patch_VialSwap.Exchange） |
| ChezhouLib | 1 | `CannotGo`（Patch_FlyRace+Patch_FloatMenuMakerMap+<>c.<Postfix>b__1_0） |
| Caravan Item Selection Enhanced | 1 | `VF_Vehicles`（CompVehicles.AddVehicleTab） |
| Land On Anything | 1 | `NEPLOA_unblockableThrustersgDesc`（LandOnAnythingMod.DoSettingsWindowContents） |
| MoeLotl Race | 1 | `ITab_Skills_Message_ShouldNotInstall_WithOutMainSkill`（MoeLotlQiSkillComp_InstallWithMainSkill.CompShouldUninstall） |

## B. 寫死的英文（最多 40 個模組）

| 模組 | 數量 | 例子 |
|---|---|---|
| Milira Addon: Fianchetto Variation | 106 | `No active drones found.`<br>`Select a pawn with FunnelBit equipment.`<br>`Select a Pawn or FunnelBit Turret first.`<br>`FunnelBit mod not found.` |
| 特性工作台 | 61 | `Copied table data to clipboard in CSV format.`<br>`Toggle fine value adjustment`<br>`Camera config`<br>`Continuous zoom` |
| Vanilla Expanded Framework | 60 | `Some changes might require you to restart the game to take e…`<br>`Export a building to xml`<br>`Export from area`<br>`Export a building to xml from area` |
| ChezhouLib | 41 | `当前筛选条件下没有 BodyAddon。`<br>`修改 Pawn 实际体型，按种族配置重新选择身体与 BodyAddon 贴图，并定位体型偏移。`<br>`临时 Addon 没有模组原始 XML 定义，可在上方移除临时 Addon。`<br>`只过滤中央预览，不修改 Addon 配置。每行右侧可单独显示或隐藏；显示全部同时清除搜索。` |
| Melee Animation | 33 | `Open new debugger`<br>`Stop`<br>`<Select Def>`<br>`Mirror X` |
| Gravship Exporter | 24 | `Export Ship as Mod`<br>`Mod Name:`<br>`Please enter a valid mod name.`<br>`✅ Mod Export Complete!` |
| Tabula Rasa | 21 | `No destination selected. Right click the gizmo to select one…`<br>`Selected destination is currently inactive, cannot recall to…`<br>`Show Old Mod Updates`<br>`By default, mod updates older than three months are hidden a…` |
| Ratkin Misc+ 鼠族拓展:边缘杂货铺 | 18 | `[DEBUG] 赠礼状态已重置`<br>`[DEBUG] 互助协议已签订`<br>`[DEBUG] 互助协议已解除`<br>`[DEBUG] 互助任务已完成` |
| Raven Race - Embers in Shadow | 16 | `Invalid Target: Enemy must be downed.`<br>`Invalid Target: Must be Humanlike.`<br>`Target is a mechanoid (Feature disabled in settings).`<br>`Must target opposite gender.` |
| Ariandel Library | 12 | `Ariandel dialogue definition is missing.`<br>`Ariandel Library`<br>`[AriandelLibrary] SituationManager is unavailable.`<br>`[AriandelLibrary] No SituationDef is loaded.` |
| [NL] Dynamic Portraits | 12 | `Enable Drawing mood bar`<br>`Enable Drawing rest and food bar`<br>`Enable Drawing Targeted Colonists`<br>`Enable Drawing Work Items` |
| RimUnity | 10 | `chezhou9413 · keeptpa · Karl-441`<br>`RimUnity · 显存归责 / 资源来源`<br>`复制全量 JSON`<br>`RimUnity · 特效渲染诊断` |
| Dubs Bad Hygiene | 10 | `Deep grid`<br>`Surface water cache`<br>`Bad Hygiene Wiki`<br>`Dubs Mods Discord` |
| Tweaks Galore | 10 | `Can Raid Lower Techs`<br>`If enabled, allows this faction to raid comparitively lower …`<br>`Can Raid Higher Techs`<br>`If enabled, allows this faction to raid comparitively higher…` |
| Wolfein Allegiance | 10 | `Reset to defaults`<br>`Map cleared.`<br>`Map cleared and Branch Office spawned.`<br>`Wolfein Garrison spawned.` |
| Monolyn Race | 9 | `Invalid count input.`<br>`Release Corpse`<br>`Release the stored corpse from storage.`<br>`Finish` |
| BertEvent - MoeLotl Event Expansion - 萌螈事件拓展 | 9 | `Flag Key:`<br>`Flag Value:`<br>`Quest:`<br>`Delay ticks:` |
| RimTalk | 9 | `▼ Runtime Variables`<br>`--- REQUEST PAYLOAD (JSON) ---`<br>`--- RESPONSE PAYLOAD (JSON) ---`<br>`No image to display.` |
| yc's Faction Editor | 9 | `Import Icon`<br>`  (Configured but not enabled)`<br>`  Skin Color:`<br>`Biotech DLC required.` |
| Milira Faction: Milira Imperium | 7 | `“把他们轰成哥斯拉！”\n\n开玩笑的，这是个测试时用到的DEV按钮，你要是直接按了那就没意思了。 `<br>`Milira: Imperium faction not found.`<br>`Milira: no airdrop sets configured.`<br>`Milira: chosen airdrop set produced no pawns.` |
| RimTalk - Expand Toddlers | 6 | `Please select a toddler first.`<br>`No other toddler found to scuffle with.`<br>`[Debug] No pawn selected`<br>`[Debug] Integration status logged. Check dev console.` |
| Humanoid Alien Races | 5 | `Alien Race`<br>`Select the race you wish to edit`<br>`Select what you want to edit`<br>`Select the lifestage you wish to edit` |
| NewRatkinPlus | 5 | `Map cleared (terrain preserved).`<br>`RaceSettings(HAR) 덤프를 Player.log에 출력했습니다.`<br>`No pawns found to fill needs.`<br>`Spawn` |
| RimTalk - Enhanced Prompt and Announcement | 5 | `提示：品质等级从低到高为：Awful < Poor < Normal < Good < Excellent < Mast…`<br>`说明：\n1. 系统会自动计算殖民地中心（基于居住区）。\n2. 提供8方位判断（东、南、西、北及四个斜向）。\n3. 区域类…`<br>`• In Bedroom, Northeast of colony (Town)`<br>`• Outdoors in Growing Zone, South of colony (Town Edge)` |
| RimTalk: Persona Director | 5 | `RimTalk not found or incompatible.`<br>`Could not determine dialogue initiator.`<br>`Failed to prepare data.`<br>`Min` |
| [NL] Facial Animation - WIP | 4 | `Output Tex Count`<br>`Outout Logs`<br>`Re-draw RenTex`<br>`Play Repeat` |
| Dubs Mint Menus | 4 | `Fin`<br>`Rst`<br>`Consumption`<br>`Production` |
| MoeLotl: Rigor Mortis | 4 | `Finish Ritual`<br>`Fail Ritual`<br>`End Ritual`<br>`Cannot use now` |
| Keyz' Allow Utilities | 4 | `<size=10%>Horiz <color=red>Spacing</color></size>`<br>`<size=10%>Vert <color=red>Spacing</color></size>`<br>`<size=10%>Horiz <color=blue>Offset</color></size>`<br>`<size=10%>Vert <color=blue>Offset</color></size>` |
| Adaptive Storage Framework | 3 | `Add stack`<br>`Edit graphics`<br>`Update graphics` |
| Custom Quest Framework | 3 | `Spawn`<br>`Try Generate`<br>`Log data` |
| MoeLotl Faction Expand | 3 | `Finish Ritual`<br>`Fail Ritual`<br>`End Ritual` |
| PA's God Hands | 3 | `全局强制 Crawling`<br>`Settings Reset`<br>`显示 JFA 轮廓诊断面片` |
| Ratkin Underground+ | 3 | `Test Event`<br>`Trigger the test event for the drilling vehicle.`<br>`Factory death quota completed!` |
| Universal Blueprints | 3 | `状态: 缓存={cacheCount} | 队列={queueCount}`<br>`Error checking agreement status.`<br>`Target Steam ID (Leave empty to use original):` |
| Hospitality (Continued) | 3 | `Reset to Defaults`<br>`Temporarily use Building_Bed's sheet-color getter, including…`<br>`Start on the affected bed, leave it visible for a few second…` |
| MoeLotl Tale: Miracle’s Echo of Justice 萌螈故事线：米拉库尔的正义回响 | 3 | `Create or unlock Miracle before firing this event.`<br>`Miracle already exists; crash was not triggered.`<br>`需要存在剧情米拉并启用 RimTalk 兼容。` |
| Nice Inventory Tab | 2 | `Can take off apparel`<br>`Compositable Loadouts:` |
| Numbers | 2 | `delet this`<br>`Cannot enable self tend because work priorities are uninitia…` |
| [OA]Ratkin Faction: Oberonia aurea | 2 | `OAInteractHandler is null`<br>`ScienceDepartmentInteractHandler is null` |
