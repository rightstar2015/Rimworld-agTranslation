using System.Linq;
using AgentTranslation.Core;
using AgentTranslation.Server;
using Xunit;

namespace AgentTranslation.Tests;

public class LanguageRegistryTests
{
    [Fact]
    public void ListsEveryShippedLanguageOnce()
    {
        var folders = TargetLanguages.All.Select(l => l.Folder).ToList();
        Assert.Equal(folders.Count, folders.Distinct().Count());
        Assert.Equal(29, folders.Count);
        Assert.All(new[] { "ChineseSimplified", "ChineseTraditional", "Japanese", "Korean", "English" }, f => Assert.True(TargetLanguages.IsOfficial(f)));
        Assert.False(TargetLanguages.IsOfficial("German"));
        Assert.False(TargetLanguages.IsValid("auto"));
        Assert.False(TargetLanguages.IsValid("../English"));
    }

    [Fact]
    public void BridgeOffersTheSameLanguages()
    {
        var bridge = AgentTranslation.Bridge.UserConfigFile.Languages.Select(l => l.Folder).Where(f => f != "auto");
        Assert.Equal(TargetLanguages.All.Select(l => l.Folder).OrderBy(x => x), bridge.OrderBy(x => x));
    }

    [Theory]
    [InlineData("Korean (한국어)", "Korean")]
    [InlineData("PortugueseBrazilian (Português Brasileiro)", "PortugueseBrazilian")]
    [InlineData("Portuguese (Português)", "Portuguese")]
    [InlineData("Spanish (Español(Castellano))", "Spanish")]
    public void MapsGameFoldersToLegacyNames(string game, string expected)
    {
        Assert.Equal(expected, TargetLanguages.FromGameLanguage(game));
    }

    [Fact]
    public void PackDescriptionUsesThePackLanguage()
    {
        Assert.Contains("日本語翻訳パック", PackBuilder.DescriptionHead("Japanese"));
        Assert.Contains("한국어 번역 팩", PackBuilder.DescriptionHead("Korean"));
        Assert.Contains("Deutsch", PackBuilder.DescriptionHead("German"));
    }
}

[Collection("CharSets")]
public class MultiLanguageValidationTests
{
    private static ValidationOutcome V(string source, string text, TargetScript script) =>
        Validator.Validate(source, text, new ValidatorOptions { Script = script });

    [Fact]
    public void JapaneseRejectsHangulAndKoreanRejectsKana()
    {
        Assert.Contains(V("Open the door", "ドアを열다", TargetScript.Japanese).Rejects, r => r.Code == "wrong_script");
        Assert.Contains(V("Open the door", "문을 開けて", TargetScript.Korean).Rejects, r => r.Code == "wrong_script");
        Assert.True(V("Open the door", "ドアを開ける", TargetScript.Japanese).Accepted);
        Assert.True(V("Open the door", "문을 열다", TargetScript.Korean).Accepted);
    }

    [Fact]
    public void FlagsUntranslatedTextForNonLatinTargets()
    {
        var o = V("Open the door now", "Open the door", TargetScript.Korean);
        Assert.True(o.Accepted);
        Assert.Contains(o.Flags, f => f.Code == "no_target_script");
    }

    [Fact]
    public void EnglishTargetFlagsLeftoverChinese()
    {
        var o = V("打开这扇门", "Open 这扇 door", TargetScript.Latin);
        Assert.True(o.Accepted);
        Assert.Contains(o.Flags, f => f.Code == "foreign_script");
        Assert.DoesNotContain(V("打开这扇门", "Open this door", TargetScript.Latin).Flags, f => f.Code == "foreign_script" || f.Code == "length_anomaly");
    }

    [Fact]
    public void TokensAreCheckedForEveryLanguage()
    {
        Assert.Contains(V("{0} has {1} items", "{0} hat Gegenstände", TargetScript.Latin).Rejects, r => r.Code == "placeholder_mismatch");
        Assert.Contains(V("Line one\\nLine two", "Строка один", TargetScript.Cyrillic).Rejects, r => r.Code == "newline_mismatch");
    }

    [Fact]
    public void LengthComparesWidthsAcrossScripts()
    {
        // Chinese source to English: three times as many characters is normal.
        Assert.DoesNotContain(V("这是一个很长的物品描述文字内容", "This is a fairly long description of the item", TargetScript.Latin).Flags, f => f.Code == "length_anomaly");
        Assert.Contains(V("This is a fairly long description of the item", "Item", TargetScript.Latin).Flags, f => f.Code == "length_anomaly");
    }

    [Fact]
    public void ChineseTargetsKeepTheirOriginalRules()
    {
        Assert.Contains(V("Open the door", "打開这扇门", TargetScript.Simplified).Rejects, r => r.Code == "traditional_chars");
        Assert.Contains(V("Open the door", "打开這扇門", TargetScript.Traditional).Rejects, r => r.Code == "simplified_chars");
    }
}

[Collection("CharSets")]
public class SourceInTargetTests
{
    [Theory]
    [InlineData("ドアを開ける", "Japanese", true)]
    [InlineData("門", "Japanese", false)]
    [InlineData("문을 열다", "Korean", true)]
    [InlineData("Open the door", "English", true)]
    [InlineData("打开门", "English", false)]
    [InlineData("Open the door", "German", false)]
    [InlineData("Открыть дверь", "Russian", true)]
    [InlineData("打开这扇门", "ChineseSimplified", true)]
    [InlineData("打開這扇門", "ChineseSimplified", false)]
    [InlineData("ドアを開ける", "ChineseSimplified", false)]
    public void JudgesBySourceScript(string source, string language, bool expected)
    {
        Assert.Equal(expected, ModScanner.IsInLanguage(source, language));
    }
}

[Collection("CharSets")]
public class MultiLanguageGlossaryTests
{
    [Fact]
    public void AcceptsAnySourceScriptAndChecksTheTarget()
    {
        Assert.Null(UserGlossary.Check("en", "治疗", "Healing", null, "English"));
        Assert.Null(UserGlossary.Check("en", "Steel", "鋼鉄", null, "Japanese"));
        Assert.NotNull(UserGlossary.Check("en", "Steel", "강철鋼鉄ア", null, "Korean"));
        Assert.NotNull(UserGlossary.Check("s2t", "钢铁", "鋼鐵", null, "Japanese"));
        Assert.NotNull(UserGlossary.Check("en", "Steel", "Stahl", null, "Klingon"));
    }
}

/// <summary>Tests that read or replace the shared Simplified/Traditional character sets run one at a time.</summary>
[CollectionDefinition("CharSets", DisableParallelization = true)]
public class CharSetsCollection { }

/// <summary>Installs the full OpenCC character sets once, as the server does at startup.</summary>
public static class FullCharSets
{
    private static readonly System.Lazy<AgentTranslation.Server.ChineseResources> Loaded = new(() => AgentTranslation.Server.ChineseResources.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rwat-no-game")));

    public static void Ensure() => _ = Loaded.Value;
}
