using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AgentTranslation.Core;
using Xunit;

namespace AgentTranslation.Tests;

public class StableIdTests
{
    [Fact]
    public void Id_IsDeterministic_AndCaseInsensitiveOnPackageId()
    {
        var a = StableId.Id("Author.Mod", EntryKind.Keyed, "Key", "Hello");
        var b = StableId.Id("author.mod", EntryKind.Keyed, "Key", "Hello");
        Assert.Equal(a, b);
        Assert.Equal(26, a.Length);
        Assert.True(StableId.LooksLikeId(a));
    }

    [Fact]
    public void Id_ChangesWithSource_KeyDoesNot()
    {
        Assert.NotEqual(StableId.Id("m", EntryKind.Keyed, "K", "a"), StableId.Id("m", EntryKind.Keyed, "K", "b"));
        Assert.Equal(StableId.Key("m", EntryKind.Keyed, "K"), StableId.Key("M", EntryKind.Keyed, "K"));
    }

    [Fact]
    public void Id_NormalizesLineEndingsOnly()
    {
        Assert.Equal(StableId.Id("m", EntryKind.Def, "p", "a\r\nb"), StableId.Id("m", EntryKind.Def, "p", "a\nb"));
        Assert.NotEqual(StableId.Id("m", EntryKind.Def, "p", "a b"), StableId.Id("m", EntryKind.Def, "p", "a  b"));
    }

    [Fact]
    public void Id_NoConcatenationCollision()
    {
        Assert.NotEqual(StableId.Key("ab", EntryKind.Keyed, "c"), StableId.Key("a", EntryKind.Keyed, "bc"));
    }
}

public class PathSandboxTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rwat-sandbox-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../x.xml")]
    [InlineData("a/../../x.xml")]
    [InlineData("C:\\Windows\\x.xml")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("/etc/passwd")]
    [InlineData("a/./b")]
    [InlineData("CON.xml")]
    [InlineData("a/nul")]
    [InlineData("a:b")]
    [InlineData("dir./x")]
    [InlineData("")]
    public void Rejects_Escapes(string rel)
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<SandboxViolationException>(() => PathSandbox.Resolve(_root, rel));
    }

    [Fact]
    public void Accepts_NormalPath()
    {
        Directory.CreateDirectory(_root);
        var p = PathSandbox.Resolve(_root, "1.7/Languages/ChineseTraditional/Keyed/x.xml");
        Assert.StartsWith(Path.GetFullPath(_root), p);
    }

    [Fact]
    public void Rejects_JunctionInPath()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetTempPath(), "rwat-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (IOException)
        {
            return; // 沒有建立符號連結的權限時略過
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        Assert.Throws<SandboxViolationException>(() => PathSandbox.Resolve(_root, "link/x.xml"));
    }
}

public class ValidatorTests
{
    [Fact]
    public void Accepts_GoodTranslation()
    {
        var r = Validator.Validate("Installed mod-version: {0}", "已安裝的模組版本：{0}");
        Assert.True(r.Accepted);
        Assert.Empty(r.Flags);
    }

    [Theory]
    [InlineData("Deals {0} damage.", "造成傷害。", "placeholder_mismatch")]
    [InlineData("Deals {0} damage.", "造成 {1} 點傷害。", "placeholder_mismatch")]
    [InlineData("{PAWN_nameDef} is hungry.", "{PAWN_name} 餓了。", "placeholder_mismatch")]
    [InlineData("Line one.\\nLine two.", "第一行。第二行。", "newline_mismatch")]
    [InlineData("<color=#ff0000>Danger</color>", "危險", "markup_mismatch")]
    [InlineData("(*Threat)Raid(/Threat) incoming", "襲擊來了", "tag_mismatch")]
    [InlineData("Turret settings here", "炮塔设置在这里", "simplified_chars")]
    [InlineData("Hello there", "   ", "empty")]
    [InlineData("Hello there", "TODO", "placeholder_text")]
    [InlineData("Hello there", "你好\u0001", "invalid_xml_chars")]
    public void Rejects(string source, string translation, string code)
    {
        var r = Validator.Validate(source, translation);
        Assert.False(r.Accepted);
        Assert.Contains(r.Rejects, i => i.Code == code);
    }

    [Fact]
    public void Conditional_AllowsTranslatedBranches()
    {
        var r = Validator.Validate("{PAWN_gender ? He : She} left.", "{PAWN_gender ? 他 : 她}離開了。");
        Assert.True(r.Accepted);
    }

    [Fact]
    public void Flags_IdenticalAndLength()
    {
        Assert.Contains(Validator.Validate("Steel wall", "Steel wall").Flags, f => f.Code == "identical_to_source");
        var opts = new ValidatorOptions();
        opts.ProperNounWhitelist.Add("Harmony");
        Assert.Empty(Validator.Validate("Harmony", "Harmony", opts).Flags);
        Assert.Contains(Validator.Validate("A long description of the item that matters.", "物", null).Flags, f => f.Code == "length_anomaly");
    }

    [Fact]
    public void Traditional_NotFlaggedAsSimplified()
    {
        Assert.True(Validator.Validate("Turret settings here", "砲塔設定在這裡，意志堅定，北斗。").Accepted);
    }
}

public class InheritanceTests
{
    private sealed class Source : INamedNodeSource
    {
        public List<DefNode> Nodes { get; } = new();
        public IEnumerable<DefNode> FindNamed(string name) => Nodes.Where(n => n.Name == name);
    }

    private static XElement Parse(string xml) => SafeXml.Parse(xml).Root!;

    [Fact]
    public void Child_InheritsAndOverrides_ListsAppend()
    {
        var src = new Source();
        var parent = new DefNode(Parse("<ThingDef Name=\"Base\" Abstract=\"True\"><description>Parent desc.</description><label>parent</label><tags><li>a</li></tags></ThingDef>"), 0, "core", "p.xml");
        var child = new DefNode(Parse("<ThingDef ParentName=\"Base\">\n  <defName>X</defName>\n  <label>child</label>\n  <tags><li>b</li></tags>\n</ThingDef>"), 1, "mod", "c.xml");
        src.Nodes.Add(parent);
        src.Nodes.Add(child);
        var r = new XmlInheritanceResolver(src).Resolve(child);
        Assert.Equal("Parent desc.", r.Element("description")!.Value);
        Assert.Equal("child", r.Element("label")!.Value);
        Assert.Equal(new[] { "a", "b" }, r.Element("tags")!.Elements("li").Select(e => e.Value));
        Assert.Null(r.Attribute("Abstract"));
    }

    [Fact]
    public void InheritFalse_Replaces()
    {
        var src = new Source();
        var parent = new DefNode(Parse("<ThingDef Name=\"Base\"><tags><li>a</li></tags></ThingDef>"), 0, "core", "p.xml");
        var child = new DefNode(Parse("<ThingDef ParentName=\"Base\"><defName>X</defName><tags Inherit=\"False\"><li>b</li></tags></ThingDef>"), 1, "mod", "c.xml");
        src.Nodes.Add(parent);
        src.Nodes.Add(child);
        var r = new XmlInheritanceResolver(src).Resolve(child);
        Assert.Equal(new[] { "b" }, r.Element("tags")!.Elements("li").Select(e => e.Value));
    }

    [Fact]
    public void Parent_ChosenByHighestLoadOrderNotAfterSelf()
    {
        var src = new Source();
        src.Nodes.Add(new DefNode(Parse("<ThingDef Name=\"Base\"><label>early</label></ThingDef>"), 0, "a", "a.xml"));
        src.Nodes.Add(new DefNode(Parse("<ThingDef Name=\"Base\"><label>middle</label></ThingDef>"), 5, "b", "b.xml"));
        src.Nodes.Add(new DefNode(Parse("<ThingDef Name=\"Base\"><label>late</label></ThingDef>"), 9, "c", "c.xml"));
        var child = new DefNode(Parse("<ThingDef ParentName=\"Base\"><defName>X</defName></ThingDef>"), 6, "mod", "x.xml");
        Assert.Equal("middle", new XmlInheritanceResolver(src).Resolve(child).Element("label")!.Value);
    }
}

public class LoadFolderTests
{
    [Fact]
    public void VersionFolder_ThenCommon_ThenRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rwat-lf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "1.5"));
        Directory.CreateDirectory(Path.Combine(root, "1.6"));
        Directory.CreateDirectory(Path.Combine(root, "Common"));
        var f = LoadFolderResolver.Resolve(root, "1.7", _ => false);
        Assert.Equal(new[] { Path.Combine(root, "1.6"), Path.Combine(root, "Common"), root }, f);
    }

    [Fact]
    public void LoadFoldersXml_RespectsIfModActive()
    {
        var root = Path.Combine(Path.GetTempPath(), "rwat-lf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "LoadFolders.xml"),
            "<loadFolders><v1.7><li>/</li><li>1.7</li><li IfModActive=\"other.mod\">Compat</li></v1.7></loadFolders>");
        var off = LoadFolderResolver.Resolve(root, "1.7", _ => false);
        Assert.Equal(new[] { Path.Combine(root, "1.7"), root }, off);
        var on = LoadFolderResolver.Resolve(root, "1.7", id => id == "other.mod");
        Assert.Equal(new[] { Path.Combine(root, "Compat"), Path.Combine(root, "1.7"), root }, on);
    }
}

public class WriterTests
{
    [Fact]
    public void Writer_EscapesAndRoundTrips()
    {
        var xml = LanguageDataWriter.Render(new[] { new LanguageDataWriter.Item("A.b", "<危險> & \"引號\"\\n第二行", "EN -- text-") });
        var doc = SafeXml.Parse(xml);
        Assert.Equal("<危險> & \"引號\"\\n第二行", doc.Root!.Element("A.b")!.Value);
    }

    [Fact]
    public void SafeXml_RejectsDtd()
    {
        Assert.ThrowsAny<System.Xml.XmlException>(() => SafeXml.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><x>&e;</x>"));
    }
}

public class SimplifiedListTests
{
    /// <summary>常見於繁體中文、也與簡體同形的字，不能出現在簡體字清單中。</summary>
    [Fact]
    public void SharedCharacters_NotFlagged()
    {
        const string shared = "修械器示存源牧志斗郁涂术杰庄厂广据撑柜横毁污温烟猫痒痴着筑脱虚静黄户网胜适气画腊盖脚挂蜡";
        Assert.Empty(SimplifiedChars.FindIn(shared));
    }
}
