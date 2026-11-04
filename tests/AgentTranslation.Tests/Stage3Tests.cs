using System;
using System.IO;
using System.Linq;
using System.Text;
using AgentTranslation.Bridge;
using AgentTranslation.Server;
using Xunit;

public class InboxRequestTests
{
    private const string Id = "abcdefghijklmnopqrstuvwxyz";

    private static InboxRequest? Parse(string json, out string? reason) => InboxRequest.TryParse(Encoding.UTF8.GetBytes(json), out reason);

    [Fact]
    public void AcceptsTheExactFormat()
    {
        var r = Parse("{\"schema\":1,\"kind\":\"reload_keyed\",\"request_id\":\"" + Id + "\"}", out _);
        Assert.NotNull(r);
        Assert.Equal(InboxRequest.KindReloadKeyed, r!.Kind);
    }

    [Fact]
    public void SerializeRoundTrips()
    {
        foreach (var kind in InboxRequest.Kinds)
        {
            var r = InboxRequest.TryParse(InboxRequest.Serialize(kind, Id), out var reason);
            Assert.True(r != null, reason);
            Assert.Equal(kind, r!.Kind);
        }
    }

    [Theory]
    [InlineData("{\"schema\":1,\"kind\":\"reload_keyed\",\"request_id\":\"" + Id + "\",\"path\":\"C:/x\"}", "unknown_or_duplicate_property")]
    [InlineData("{\"schema\":1,\"kind\":\"run_command\",\"request_id\":\"" + Id + "\"}", "unknown_kind")]
    [InlineData("{\"schema\":2,\"kind\":\"reload_keyed\",\"request_id\":\"" + Id + "\"}", "schema")]
    [InlineData("{\"schema\":1,\"kind\":\"reload_keyed\",\"request_id\":\"../../x\"}", "request_id")]
    [InlineData("{\"schema\":1,\"kind\":\"reload_keyed\"}", "missing_property")]
    [InlineData("{\"schema\":1,\"kind\":\"reload_keyed\",\"kind\":\"reload_keyed\",\"request_id\":\"" + Id + "\"}", "unknown_or_duplicate_property")]
    [InlineData("[1,2,3]", "not_an_object")]
    [InlineData("{\"schema\":1,", "invalid_json")]
    [InlineData("{\"schema\":\"1\",\"kind\":\"reload_keyed\",\"request_id\":\"" + Id + "\"}", "schema")]
    public void RejectsEverythingElse(string json, string expected)
    {
        Assert.Null(Parse(json, out var reason));
        Assert.Equal(expected, reason);
    }

    [Fact]
    public void RejectsOversizedFiles()
    {
        Assert.Null(InboxRequest.TryParse(new byte[InboxRequest.MaxBytes + 1], out var reason));
        Assert.Equal("size", reason);
    }

    [Theory]
    [InlineData("req_" + Id + ".json", true)]
    [InlineData("req_" + Id + ".json.tmp", false)]
    [InlineData("evil.json", false)]
    [InlineData("req_ABCDEFGHIJKLMNOPQRSTUVWXYZ.json", false)]
    public void RequestFileNames(string name, bool ok) => Assert.Equal(ok, InboxRequest.IsRequestFileName(name));
}

public class PackReaderTests
{
    private static string MakePack(string manifestEntries, params (string Rel, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PackReader.ManifestName), "{\"tool\":\"rwat\",\"built_utc\":\"2026-11-04T00:00:00Z\",\"entries\":[" + manifestEntries + "]}");
        foreach (var (rel, content) in files)
        {
            var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return dir;
    }

    [Fact]
    public void ReadsEntriesWithGameNewlineHandling()
    {
        var dir = MakePack(
            "{\"id\":\"a\",\"package_id\":\"x.y\",\"kind\":\"keyed\",\"path\":\"K.One\",\"file\":\"1.7/Languages/ChineseSimplified/Keyed/x.y/K.xml\"}," +
            "{\"id\":\"b\",\"package_id\":\"x.y\",\"kind\":\"def\",\"path\":\"Thing.label\",\"file\":\"1.7/Languages/ChineseSimplified/DefInjected/ThingDef/x.y/T.xml\"}",
            ("1.7/Languages/ChineseSimplified/Keyed/x.y/K.xml", "<LanguageData><!-- EN: a --><K.One>第一行\\n第二行</K.One></LanguageData>"),
            ("1.7/Languages/ChineseSimplified/DefInjected/ThingDef/x.y/T.xml", "<LanguageData><Thing.label>东西</Thing.label></LanguageData>"));
        try
        {
            Assert.Equal(dir, PackReader.FindPackRoot(new[] { Path.GetTempPath(), dir }));
            var snap = PackReader.Read(dir);
            Assert.Equal("ChineseSimplified", snap.LanguageFolder);
            Assert.Equal("2026-11-04T00:00:00Z", snap.BuiltUtc);
            var keyed = snap.Entries.Single(e => e.Id == "a");
            Assert.Equal("第一行\n第二行", keyed.Expected);
            Assert.Equal("ThingDef", snap.Entries.Single(e => e.Id == "b").DefType);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IgnoresManifestPathsOutsideThePack()
    {
        var dir = MakePack(
            "{\"id\":\"a\",\"package_id\":\"x.y\",\"kind\":\"keyed\",\"path\":\"K\",\"file\":\"1.7/Languages/ChineseSimplified/../../../outside.xml\"}," +
            "{\"id\":\"b\",\"package_id\":\"x.y\",\"kind\":\"keyed\",\"path\":\"K\",\"file\":\"C:/Windows/win.ini\"}");
        try
        {
            Assert.Empty(PackReader.Read(dir).Entries);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RejectsDtd()
    {
        var dir = MakePack(
            "{\"id\":\"a\",\"package_id\":\"x.y\",\"kind\":\"keyed\",\"path\":\"K\",\"file\":\"1.7/Languages/ChineseSimplified/Keyed/x.y/K.xml\"}",
            ("1.7/Languages/ChineseSimplified/Keyed/x.y/K.xml", "<!DOCTYPE x [<!ENTITY e \"boom\">]><LanguageData><K>&e;</K></LanguageData>"));
        try
        {
            Assert.Empty(PackReader.Read(dir).Entries);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FolderWithoutRwatManifestIsNotAPack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PackReader.ManifestName), "{\"tool\":\"other\"}");
        try
        {
            Assert.Null(PackReader.FindPackRoot(new[] { dir }));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

[Collection("BridgeDirEnv")]
public class BridgeChannelTests
{
    [Fact]
    public void WritesRequestsTheBridgeAccepts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-bridge-" + Guid.NewGuid().ToString("N"));
        var old = Environment.GetEnvironmentVariable("RWAT_BRIDGE_DIR");
        Environment.SetEnvironmentVariable("RWAT_BRIDGE_DIR", dir);
        try
        {
            var ch = new BridgeChannel();
            var id = ch.WriteRequest(InboxRequest.KindReloadKeyed);
            var file = Directory.GetFiles(Path.Combine(dir, "inbox")).Single();
            Assert.True(InboxRequest.IsRequestFileName(Path.GetFileName(file)));
            var parsed = InboxRequest.TryParse(File.ReadAllBytes(file), out var reason);
            Assert.True(parsed != null, reason);
            Assert.Equal(id, parsed!.RequestId);
            Assert.Equal(1, ch.PendingRequests());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RWAT_BRIDGE_DIR", old);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReadsReportDefensively()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rwat-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "outbox"));
        var good = "aaaaaaaaaaaaaaaaaaaaaaaaaa";
        File.WriteAllText(Path.Combine(dir, "outbox", "runtime.json"),
            "{\"schema\":1,\"generated_utc\":\"2026-11-04T10:00:00Z\",\"language_folder\":\"ChineseSimplified (简体中文)\",\"translation_error_count\":1314," +
            "\"pack\":{\"found\":true,\"manifest_built_utc\":\"X\",\"language_matches_game\":true}," +
            "\"summary\":{\"total\":2,\"ok\":1}," +
            "\"entries\":[{\"id\":\"" + good + "\",\"status\":\"ok\"},{\"id\":\"../bad\",\"status\":\"ok\"},{\"id\":\"" + good.Replace('a', 'b') + "\",\"status\":\"overridden\",\"source\":\"other.mod\"},{\"id\":\"" + good.Replace('a', 'c') + "\",\"status\":\"changed_at_runtime\",\"source\":\"runtime\",\"relation\":\"appended\",\"actual\":\"" + new string('x', 500) + "\"}]}");
        var old = Environment.GetEnvironmentVariable("RWAT_BRIDGE_DIR");
        Environment.SetEnvironmentVariable("RWAT_BRIDGE_DIR", dir);
        try
        {
            var r = new BridgeChannel().ReadReport();
            Assert.NotNull(r);
            Assert.True(r!.PackFound);
            Assert.Equal(1314, r.TranslationErrorCount);
            Assert.Equal(3, r.Entries.Count);
            Assert.Equal("other.mod", r.Entries[good.Replace('a', 'b')].Source);
            var changed = r.Entries[good.Replace('a', 'c')];
            Assert.Equal("appended", changed.Relation);
            Assert.Equal(400, changed.Actual!.Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RWAT_BRIDGE_DIR", old);
            Directory.Delete(dir, true);
        }
    }
}

[CollectionDefinition("BridgeDirEnv", DisableParallelization = true)]
public class BridgeDirEnvCollection
{
}
