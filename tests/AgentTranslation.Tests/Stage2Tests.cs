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
        Assert.NotNull(UserGlossary.Check(kind, source, target, null));
    }

    [Theory]
    [InlineData("s2t", "添加", "新增")]
    [InlineData("en", "Plasteel", "塑鋼")]
    [InlineData("en", "mech-serum", "機械血清")]
    public void AcceptsNormalEntries(string kind, string source, string target)
    {
        Assert.Null(UserGlossary.Check(kind, source, target, null));
    }

    [Fact]
    public void AgentCannotOverwriteUserEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var g = UserGlossary.Load(dir);
            g.Add("s2t", "添加", "新增", null, "user");
            Assert.Throws<ArgumentException>(() => g.Add("s2t", "添加", "加入", null, "agent"));
            var reloaded = UserGlossary.Load(dir);
            Assert.Equal("新增", reloaded.Effective.Single(e => e.Source == "添加").Target);
            // 內建詞仍在，且可被使用者覆蓋。
            Assert.Contains(reloaded.Effective, e => e.Source == "身份" && e.Origin == "builtin");
            reloaded.Add("s2t", "身份", "身份", null, "user");
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
