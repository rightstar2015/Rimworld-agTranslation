using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AgentTranslation.Bridge;

/// <summary>
/// A request read from the bridge inbox. The format is fixed and deliberately poor: a schema number, one of a few
/// request kinds and a request id. There is no path, type name, method name or free text that could steer the game.
/// This file has no game dependency so the tests and the MCP server can share it.
/// </summary>
public sealed class InboxRequest
{
    public const int Schema = 1;
    public const int MaxBytes = 64 * 1024;
    public const int MaxFilesPerPass = 16;

    /// <summary>Regenerate the runtime report now.</summary>
    public const string KindRefreshReport = "refresh_report";
    /// <summary>Experimental: reload the pack's Keyed files into the active language (only when the user enabled it in game).</summary>
    public const string KindReloadKeyed = "reload_keyed";

    public static readonly IReadOnlyList<string> Kinds = new[] { KindRefreshReport, KindReloadKeyed };

    private static readonly string[] AllowedProperties = { "schema", "kind", "request_id" };

    public string Kind { get; }
    public string RequestId { get; }

    private InboxRequest(string kind, string requestId)
    {
        Kind = kind;
        RequestId = requestId;
    }

    public static bool IsRequestId(string? id) =>
        id != null && id.Length == 26 && id.All(c => c is >= 'a' and <= 'z' or >= '2' and <= '7');

    /// <summary>Parses and validates a request. Returns null and a reason (never the content) when invalid.</summary>
    public static InboxRequest? TryParse(byte[] bytes, out string? reason)
    {
        reason = null;
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            reason = "size";
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                reason = "not_an_object";
                return null;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in root.EnumerateObject())
            {
                if (!AllowedProperties.Contains(p.Name, StringComparer.Ordinal) || !names.Add(p.Name))
                {
                    reason = "unknown_or_duplicate_property";
                    return null;
                }
            }
            if (names.Count != AllowedProperties.Length)
            {
                reason = "missing_property";
                return null;
            }
            if (root.GetProperty("schema").ValueKind != JsonValueKind.Number || !root.GetProperty("schema").TryGetInt32(out var schema) || schema != Schema)
            {
                reason = "schema";
                return null;
            }
            var kindEl = root.GetProperty("kind");
            var idEl = root.GetProperty("request_id");
            if (kindEl.ValueKind != JsonValueKind.String || idEl.ValueKind != JsonValueKind.String)
            {
                reason = "type";
                return null;
            }
            var kind = kindEl.GetString()!;
            var id = idEl.GetString()!;
            if (!Kinds.Contains(kind, StringComparer.Ordinal))
            {
                reason = "unknown_kind";
                return null;
            }
            if (!IsRequestId(id))
            {
                reason = "request_id";
                return null;
            }
            return new InboxRequest(kind, id);
        }
        catch (JsonException)
        {
            reason = "invalid_json";
            return null;
        }
    }

    /// <summary>Serialises a request in the exact accepted format (used by the MCP server).</summary>
    public static byte[] Serialize(string kind, string requestId)
    {
        if (!Kinds.Contains(kind, StringComparer.Ordinal) || !IsRequestId(requestId))
        {
            throw new ArgumentException("Invalid request.");
        }
        return Encoding.UTF8.GetBytes("{\"schema\":" + Schema + ",\"kind\":\"" + kind + "\",\"request_id\":\"" + requestId + "\"}");
    }

    /// <summary>Inbox file names the bridge accepts: "req_" + request id + ".json".</summary>
    public static bool IsRequestFileName(string name) =>
        name.Length == 4 + 26 + 5 && name.StartsWith("req_", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) && IsRequestId(name.Substring(4, 26));
}
