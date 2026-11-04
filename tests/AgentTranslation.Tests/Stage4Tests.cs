using System.Linq;
using AgentTranslation.Core;
using AgentTranslation.Server;
using Xunit;

// Minimal stand-ins with the game's type and member names, so the scanner can be tested on this assembly's own IL.
namespace Verse
{
    public struct NamedArgument
    {
        public object Arg;
        public string Label;

        public NamedArgument(object arg, string label)
        {
            Arg = arg;
            Label = label;
        }

        public static implicit operator NamedArgument(int value) => new NamedArgument(value, string.Empty);
    }

    public static class NamedArgumentUtility
    {
        public static NamedArgument Named(this object arg, string label) => new NamedArgument(arg, label);
    }

    public static class Translator
    {
        public static string Translate(this string key) => key;

        public static bool TryTranslate(this string key, out string result)
        {
            result = key;
            return true;
        }
    }

    public static class TranslatorFormattedStringExtensions
    {
        public static string Translate(this string key, NamedArgument arg1) => key;

        public static string Translate(this string key, params NamedArgument[] args) => key;
    }

    public static class Widgets
    {
        public static void Label(string label)
        {
        }
    }
}

namespace Stage4Fixture
{
    using Verse;

    public static class SampleModCode
    {
        public static void Plain() => Use("Sample.PlainKey".Translate(), "Sample.Other".Translate());

        public static void WithNamedArgument(object pawn) => Use("Sample.WithPawn".Translate(pawn.Named("PAWN")));

        public static void WithValue(int count) => Use("Sample.WithCount".Translate(count));

        public static void WithParams(object a, object b, object c) => Use("Sample.Params".Translate(a.Named("A"), b.Named("B"), c.Named("C")));

        public static void Fallback()
        {
            if ("Sample.TryKey".TryTranslate(out var s))
            {
                Use(s);
            }
        }

        public static void Hardcoded()
        {
            Widgets.Label("Add tag");
            Widgets.Label("DEV: Spawn thing");
            Widgets.Label("SomeIdentifier_x");
        }

        public static void Dynamic(string suffix) => Use(("Sample.Dyn." + suffix).Translate());

        private static void Use(params string[] _)
        {
        }
    }
}

public class CodeStringScannerTests
{
    private static CodeStrings ScanFixture()
    {
        var all = CodeStringScanner.Scan(new[] { typeof(Stage4Fixture.SampleModCode).Assembly.Location });
        var result = new CodeStrings();
        result.Keys.AddRange(all.Keys.Where(k => k.Type == "Stage4Fixture.SampleModCode"));
        result.Literals.AddRange(all.Literals.Where(l => l.Type == "Stage4Fixture.SampleModCode"));
        return result;
    }

    [Fact]
    public void FindsLiteralKeysWithArgumentCounts()
    {
        var keys = ScanFixture().Keys.ToDictionary(k => k.Key, k => k);
        Assert.Equal(0, keys["Sample.PlainKey"].ArgCount);
        Assert.Equal(1, keys["Sample.WithPawn"].ArgCount);
        Assert.Equal(1, keys["Sample.WithCount"].ArgCount);
        Assert.Null(keys["Sample.Params"].ArgCount);
        Assert.Contains("Sample.Other", keys["Sample.PlainKey"].NearbyKeys);
        Assert.Equal("Plain", keys["Sample.PlainKey"].Method);
    }

    [Fact]
    public void NeverTakesArgumentNamesTryTranslateOrDynamicKeys()
    {
        var keys = ScanFixture().Keys.Select(k => k.Key).ToHashSet();
        Assert.DoesNotContain("PAWN", keys);
        Assert.DoesNotContain("A", keys);
        Assert.DoesNotContain("C", keys);
        Assert.DoesNotContain("Sample.TryKey", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("Sample.Dyn"));
    }

    [Fact]
    public void ReportsHardcodedUiTextButNotDevOrIdentifiers()
    {
        var literals = ScanFixture().Literals.Select(l => l.Text).ToList();
        Assert.Contains("Add tag", literals);
        Assert.DoesNotContain("DEV: Spawn thing", literals);
        Assert.DoesNotContain("SomeIdentifier_x", literals);
    }

    [Fact]
    public void IgnoresMissingAndNonAssemblyFiles()
    {
        var r = CodeStringScanner.Scan(new[] { "Z:/does/not/exist.dll", typeof(CodeStringScannerTests).Assembly.Location + ".missing" });
        Assert.Empty(r.Keys);
    }
}

public class TargetLanguageAutoTests
{
    [Theory]
    [InlineData("auto", "ChineseTraditional (繁體中文)", "ChineseTraditional", "game_language")]
    [InlineData("auto", "ChineseSimplified (简体中文)", "ChineseSimplified", "game_language")]
    [InlineData("auto", "English", "ChineseSimplified", "default (game language is not Chinese)")]
    [InlineData("auto", null, "ChineseSimplified", "default (game language is not Chinese)")]
    [InlineData("ChineseSimplified", "ChineseTraditional (繁體中文)", "ChineseSimplified", "config")]
    public void PicksTheEffectiveLanguage(string setting, string? game, string expected, string source)
    {
        var c = new AppConfig { TargetLanguageSetting = setting };
        c.ResolveTargetLanguage(game);
        Assert.Equal(expected, c.TargetLanguage);
        Assert.Equal(source, c.TargetLanguageSource);
    }

    [Fact]
    public void NewConfigsFollowTheGame() => Assert.Equal(TargetLanguages.Auto, new AppConfig().TargetLanguageSetting);
}

public class CodeKeyValidationTests
{
    private static readonly ValidatorOptions Sc = new ValidatorOptions { Script = TargetScript.Simplified };

    [Fact]
    public void AcceptsPlainTextAndAlwaysFlagsInferred()
    {
        var o = Validator.ValidateCodeKey("重置", 0, Sc);
        Assert.True(o.Accepted);
        Assert.Contains(o.Flags, f => f.Code == "code_key_inferred");
    }

    [Fact]
    public void RejectsPlaceholdersWhenCodePassesNoArguments()
    {
        Assert.Contains(Validator.ValidateCodeKey("重置 {0}", 0, Sc).Rejects, r => r.Code == "placeholder_mismatch");
    }

    [Fact]
    public void ChecksPlaceholderRangeWhenCountIsKnown()
    {
        Assert.True(Validator.ValidateCodeKey("数量：{0}", 1, Sc).Accepted);
        Assert.Contains(Validator.ValidateCodeKey("{0} 与 {1}", 1, Sc).Rejects, r => r.Code == "placeholder_mismatch");
        Assert.True(Validator.ValidateCodeKey("{PAWN_label} 来了", null, Sc).Accepted);
    }

    [Fact]
    public void KeepsGeneralChecks()
    {
        Assert.Contains(Validator.ValidateCodeKey(" ", 0, Sc).Rejects, r => r.Code == "empty");
        Assert.Contains(Validator.ValidateCodeKey("設定", 0, Sc).Rejects, r => r.Code == "traditional_chars");
    }
}
