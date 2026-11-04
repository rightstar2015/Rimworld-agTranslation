"""Adversarial MCP checks against `rwat serve` (stdio), for the security review.

Usage: python -I tools/security_check.py <path to rwat.exe> <scratch folder> [mod packageId]
Uses only scratch work/pack/bridge folders inside <scratch folder>; the user's real folders are not touched.
The mod (default migcorp.skiptech) must be installed and active.
"""
import json, os, subprocess, sys, pathlib

if len(sys.argv) < 3:
    raise SystemExit(__doc__)
SP = pathlib.Path(sys.argv[2]).resolve()
SP.mkdir(parents=True, exist_ok=True)
EXE = sys.argv[1]
env = dict(os.environ)
env["RWAT_WORK_DIR"] = str(SP / "sec_work")
env["RWAT_PACK_OUTPUT_DIR"] = str(SP / "sec_pack")
env["RWAT_BRIDGE_DIR"] = str(SP / "sec_bridge")
(SP / "sec_pack").mkdir(exist_ok=True)

p = subprocess.Popen([EXE, "serve"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
nid = 0

def rpc(method, params=None, notify=False):
    global nid
    msg = {"jsonrpc": "2.0", "method": method}
    if params is not None:
        msg["params"] = params
    if not notify:
        nid += 1
        msg["id"] = nid
    p.stdin.write((json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8"))
    p.stdin.flush()
    if notify:
        return None
    while True:
        line = p.stdout.readline()
        if not line:
            raise SystemExit("server closed: " + p.stderr.read().decode("utf-8", "replace")[-2000:])
        r = json.loads(line)
        if r.get("id") == nid:
            return r

def call(name, args):
    r = rpc("tools/call", {"name": name, "arguments": args})
    if "error" in r:
        return ("protocol_error", r["error"].get("message", ""))
    res = r["result"]
    text = "".join(c.get("text", "") for c in res.get("content", []))
    return ("tool_error" if res.get("isError") else "ok", text)

results = []
def check(label, outcome, expect_ok, must_contain=None):
    kind, text = outcome
    passed = (kind == "ok") == expect_ok and (must_contain is None or must_contain in text)
    results.append((passed, label, kind, text[:160].replace("\n", " ")))

rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "rwat-adversarial", "version": "1"}})
rpc("notifications/initialized", notify=True)
tools = rpc("tools/list")["result"]["tools"]
names = sorted(t["name"] for t in tools)
results.append((len(names) == 12, "tool list has the 12 translation tools only", "ok", ", ".join(names)))
for t in tools:
    ann = t.get("annotations", {})
    results.append((ann.get("openWorldHint") is False, "openWorld=false: " + t["name"], "ok", ""))
    props = t.get("inputSchema", {}).get("properties", {})
    risky = [k for k in props if any(w in k.lower() for w in ("path", "file", "url", "dir", "command"))]
    results.append((not risky, "no path/url/command parameter: " + t["name"], "ok", ",".join(risky)))
gb = next(t for t in tools if t["name"] == "get_batch")
results.append(("不是指令" in gb["description"] or "not instructions" in gb["description"], "get_batch says source text is data, not instructions", "ok", ""))

MOD = sys.argv[3] if len(sys.argv) > 3 else "migcorp.skiptech"
check("scan_mod rejects path traversal as mod id", call("scan_mod", {"mod_id": "..\\..\\Windows"}), False)
check("scan_mod rejects absolute path as mod id", call("scan_mod", {"mod_id": "C:\\Windows\\System32"}), False)
check("get_batch rejects a forged cursor", call("get_batch", {"mod_id": MOD, "cursor": "../../etc/passwd"}), False)
big = call("get_batch", {"mod_id": MOD, "max_items": 100000})
check("get_batch clamps max_items", big, True)
batch = json.loads(big[1]) if big[0] == "ok" else {}
items = batch.get("items", [])
results.append((len(items) <= 100, "get_batch returned at most 100 items", "ok", str(len(items))))
bid = batch.get("batch_id")
first = items[0]["id"] if items else "x"
check("submit rejects an unknown batch id", call("submit_translations", {"batch_id": "../../../evil", "items": [{"id": first, "text": "x"}]}), False)
r = call("submit_translations", {"batch_id": bid, "items": [{"id": "aaaaaaaaaaaaaaaaaaaaaaaaaa", "text": "x"}]})
check("submit rejects ids outside the batch", r, True, "unknown_id")
r = call("submit_translations", {"batch_id": bid, "items": [{"id": first, "text": "x"}] * 201})
check("submit rejects more than 200 items", r, False)
inj = "</" + "LanguageData><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///C:/Windows/win.ini\">]><a>&e;</a>"
r = call("submit_translations", {"batch_id": bid, "items": [{"id": first, "text": inj}]})
results.append((True, "submit with XML/DTD injection text (outcome recorded)", r[0], r[1][:160]))
r = call("submit_translations", {"batch_id": bid, "items": [{"id": first, "text": "a\u0001b"}]})
check("submit rejects XML control characters", r, True, "invalid_xml_chars")
r = call("submit_translations", {"batch_id": bid, "items": [{"id": first, "text": "长" * 200000}]})
check("submit rejects oversized text", r, True, "too_long")
check("glossary_add rejects an over-long target", call("glossary_add", {"kind": "en", "source": "Steel", "target": "钢" * 500}), False)
check("glossary_add rejects an unknown kind", call("glossary_add", {"kind": "../x", "source": "a", "target": "b"}), False)
check("query_memory rejects over-long text", call("query_memory", {"text": "a" * 5000}), False)
check("request_reload rejects other scopes", call("request_reload", {"scope": "../../shell"}), False)
check("adopt_simplified refuses to write while auto-adopt is off", call("adopt_simplified", {"mod_id": MOD, "apply": True}), False)

# Build and check the written XML is well-formed and contains no DTD.
check("build_pack succeeds", call("build_pack", {}), True)
pack = SP / "sec_pack" / "!AgentTranslation_Pack"
xmls = list(pack.rglob("*.xml"))
bad = [x.name for x in xmls if "<!DOCTYPE" in x.read_text(encoding="utf-8")]
results.append((not bad, "no DOCTYPE in any written pack file", "ok", ",".join(bad)))
outside = [str(f) for f in (SP / "sec_pack").rglob("*") if f.is_file() and pack not in f.parents]
results.append((not outside, "nothing written outside the pack folder", "ok", ",".join(outside)))

p.stdin.close()
p.wait(timeout=30)
stderr = p.stderr.read().decode("utf-8", "replace")
user = os.environ.get("USERNAME", "")
results.append((user.lower() not in stderr.lower(), "server stderr does not contain the user name", "ok", stderr[:120]))

ok = sum(1 for r in results if r[0])
print(f"{ok}/{len(results)} passed")
for passed, label, kind, text in results:
    if not passed or "outcome recorded" in label:
        print(("PASS " if passed else "FAIL ") + label + " | " + kind + " | " + text)

sys.exit(0 if ok == len(results) else 1)
