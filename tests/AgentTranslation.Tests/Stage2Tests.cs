using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentTranslation.Core;
using AgentTranslation.Server;
using Xunit;

public class GlossaryRuleTests
{
    [Theory]
    [InlineData("s2t", "的", "得")]          // 單字規則會大範圍改寫，拒絕
    [InlineData("s2t", "添加", "添加简")]    // 譯名含簡體字
    [InlineData("s2t", "abc", "新增")]       // s2t 來源必須是中文
    [InlineData("en", "Steel", "<b>鋼</b>")] // 譯名不可含標記
    [InlineData("en", "Steel{0}", "鋼")]     // 來源不可含佔位符
    [InlineData("xx", "Steel", "鋼")]        // 未知 kind
    public void RejectsUnsafeEntries(string kind, string source, string target)
    {
        Assert.NotNull(UserGlossary.Check(kind, source, target, null, TargetLanguages.Traditional));
    }

    [Theory]
    [InlineData("s2t", "添加", "新增")]
    [InlineData("en", "Plasteel", "塑鋼")]
    [InlineData("en", "mech-serum", "機械血清")]
    public void AcceptsNormalEntries(string kind, string source, string target)
    {
        Assert.Null(UserGlossary.Check(kind, source, target, null, TargetLanguages.Traditional));
    }

    [Fact]
    public void AgentCannotOverwriteUserEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var g = UserGlossary.Load(dir);
            g.Add("s2t", "添加", "新增", null, "user", TargetLanguages.Traditional);
            Assert.Throws<ArgumentException>(() => g.Add("s2t", "添加", "加入", null, "agent", TargetLanguages.Traditional));
            var reloaded = UserGlossary.Load(dir);
            Assert.Equal("新增", reloaded.Effective.Single(e => e.Source == "添加").Target);
            // 內建詞仍在，且可被使用者覆蓋。
            Assert.Contains(reloaded.Effective, e => e.Source == "身份" && e.Origin == "builtin");
            reloaded.Add("s2t", "身份", "身份", null, "user", TargetLanguages.Traditional);
            Assert.Equal("user", UserGlossary.Load(dir).Effective.Single(e => e.Source == "身份").Origin);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void HandEditedInvalidEntriesAreIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "glossary.json"), "[{\"kind\":\"s2t\",\"source\":\"的\",\"target\":\"得\"},{\"kind\":\"en\",\"source\":\"Steel\",\"target\":\"鋼鐵\"}]");
            var g = UserGlossary.Load(dir);
            Assert.Single(g.Entries);
            Assert.Equal("Steel", g.Entries[0].Source);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class AutoAdoptRuleTests
{
    [Fact]
    public void DisabledByDefault()
    {
        var cfg = new SimplifiedAutoAdopt();
        var rule = cfg.RuleFor("any.mod");
        Assert.False(rule.Enabled);
        Assert.Equal(80, rule.MinTrust);
        Assert.True(rule.RequireConfirmedFreshness);
    }

    [Fact]
    public void PerModOverridesOnlyGivenFields()
    {
        var cfg = new SimplifiedAutoAdopt { Enabled = true };
        cfg.PerMod["Author.Mod"] = new AdoptRule { MinTrust = 90, Kinds = new List<string> { "keyed" } };
        cfg.PerMod["off.mod"] = new AdoptRule { Enabled = false };
        var r = cfg.RuleFor("author.mod");
        Assert.True(r.Enabled);
        Assert.Equal(90, r.MinTrust);
        Assert.True(r.RequireConfirmedFreshness);
        Assert.Equal(new[] { "keyed" }, r.Kinds);
        Assert.False(cfg.RuleFor("off.mod").Enabled);
        Assert.True(cfg.RuleFor("other.mod").Enabled);
    }

    [Fact]
    public void MasterSwitchWins()
    {
        var cfg = new SimplifiedAutoAdopt { Enabled = false };
        cfg.PerMod["a.b"] = new AdoptRule { Enabled = true };
        Assert.False(cfg.RuleFor("a.b").Enabled);
    }
}

public class UserGlossaryConversionTests
{
    private static ChineseConverter Make(params (string, string)[] glossary)
    {
        PhraseDictionary D(string text) => PhraseDictionary.Parse(new StringReader(text));
        var st = D("添\t添\n加\t加\n快\t快\n捷\t捷\n栏\t欄\n");
        var g = new PhraseDictionary();
        foreach (var (s, t) in glossary)
        {
            g.Add(s, new[] { t });
        }
        return new ChineseConverter(D(""), st, new[] { D(""), D(""), D("") }, g);
    }

    [Fact]
    public void UserTermTakesPriorityAndIsMarked()
    {
        var r = Make(("添加", "新增")).Convert("添加快捷栏");
        Assert.Equal("新增快捷欄", r.Text);
        Assert.Contains(r.GlossarySegments, s => s.Source == "添加");
    }
}

public class LanguageModeTests
{
    [Fact]
    public void SimplifiedModeRejectsTraditionalResidue()
    {
        var o = Validator.Validate("Steel wall", "鋼鐵墙", new ValidatorOptions { Script = TargetScript.Simplified });
        Assert.Contains(o.Rejects, r => r.Code == "traditional_chars");
        Assert.True(Validator.Validate("Steel wall", "钢铁墙", new ValidatorOptions { Script = TargetScript.Simplified }).Accepted);
    }

    [Fact]
    public void TraditionalModeRejectsSimplified()
    {
        var o = Validator.Validate("Steel wall", "钢铁牆", new ValidatorOptions { Script = TargetScript.Traditional });
        Assert.Contains(o.Rejects, r => r.Code == "simplified_chars");
        Assert.True(Validator.Validate("Steel wall", "鋼鐵牆", new ValidatorOptions { Script = TargetScript.Traditional }).Accepted);
    }

    [Fact]
    public void DefaultTargetIsSimplified()
    {
        Assert.Equal(TargetLanguages.Simplified, new AppConfig().TargetLanguage);
        Assert.Equal(TargetScript.Simplified, new ValidatorOptions().Script);
        Assert.Equal("Agent Translation Pack (简体中文)", new AppConfig().EffectivePackDisplayName);
    }

    [Theory]
    [InlineData("ChineseSimplified", "ChineseSimplified", true)]
    [InlineData("ChineseSimplified (简体中文)", "ChineseSimplified", true)]
    [InlineData("ChineseSimplifiedX", "ChineseSimplified", false)]
    [InlineData("ChineseTraditional (繁體中文)", "ChineseSimplified", false)]
    public void FolderMatching(string folder, string language, bool expected)
    {
        Assert.Equal(expected, TargetLanguages.FolderMatches(folder, language));
    }

    [Fact]
    public void GlossaryEntriesAreSeparatedByLanguage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var g = UserGlossary.Load(dir);
            g.Add("en", "Steel", "钢铁", null, "user", TargetLanguages.Simplified);
            g.Add("en", "Steel", "鋼鐵", null, "user", TargetLanguages.Traditional);
            Assert.Throws<ArgumentException>(() => g.Add("en", "Wall", "牆", null, "user", TargetLanguages.Simplified));
            var reloaded = UserGlossary.Load(dir);
            Assert.Equal("钢铁", reloaded.For(TargetLanguages.Simplified).Single(e => e.Kind == "en").Target);
            Assert.Equal("鋼鐵", reloaded.For(TargetLanguages.Traditional).Single(e => e.Kind == "en").Target);
            // s2t terms only apply in Traditional mode.
            Assert.DoesNotContain(reloaded.For(TargetLanguages.Simplified), e => e.Kind == "s2t");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LegacyTranslationsMoveToTraditionalFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "translations"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "translations", "a.b.json"), "{\"package_id\":\"a.b\",\"records\":{}}");
            var sc = new TranslationStore(dir, TargetLanguages.Simplified);
            Assert.True(File.Exists(Path.Combine(dir, "translations", TargetLanguages.Traditional, "a.b.json")));
            Assert.Empty(sc.AllPackageIds());
            Assert.Contains("a.b", new TranslationStore(dir, TargetLanguages.Traditional).AllPackageIds());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
public class PathConflictTests
{
    private static ExistingTranslationIndex Index(params string[] paths)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-test-" + Guid.NewGuid().ToString("N"));
        var defDir = Path.Combine(dir, "DefInjected", "HediffDef");
        Directory.CreateDirectory(defDir);
        File.WriteAllText(Path.Combine(defDir, "a.xml"), "<LanguageData>" + string.Concat(paths.Select(p => "<" + p + ">x</" + p + ">")) + "</LanguageData>");
        var index = new ExistingTranslationIndex();
        index.AddLanguageFolder(dir);
        Directory.Delete(dir, true);
        return index;
    }

    private static SourceEntry Entry(string path) => new("a.b", EntryKind.Def, "HediffDef", path, "text", "Defs/a.xml");

    [Fact]
    public void PlainFieldOfTouchedDefIsNotAmbiguous()
    {
        var index = Index("Flu.label");
        Assert.False(index.TouchesDef(Entry("Flu.description")));
    }

    [Fact]
    public void HandlePathMakesIndexPathAmbiguous()
    {
        var index = Index("Flu.stages.Minor.label");
        Assert.True(index.TouchesDef(Entry("Flu.stages.0.label")));
        Assert.False(index.TouchesDef(Entry("Flu.comps.0.label")));
    }

    [Fact]
    public void IndexPathsDoNotConflictWithEachOther()
    {
        var index = Index("Flu.stages.0.label");
        Assert.False(index.TouchesDef(Entry("Flu.stages.1.label")));
    }

    [Fact]
    public void FullListInjectionMakesItemsAmbiguous()
    {
        var index = Index("Flu.rulesStrings");
        Assert.True(index.TouchesDef(Entry("Flu.rulesStrings.0")));
    }
}