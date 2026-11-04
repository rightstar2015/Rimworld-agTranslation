"""最小 MCP stdio 用戶端：initialize → tools/list → 呼叫幾個唯讀工具。只用標準函式庫。

用法：python -I mcp_smoke.py <rwat.exe 路徑> [packageId]
"""
import json
import subprocess
import sys


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    exe = sys.argv[1]
    mod = sys.argv[2] if len(sys.argv) > 2 else None
    proc = subprocess.Popen([exe, "serve"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    next_id = 0

    def send(method, params=None, notify=False):
        nonlocal next_id
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            next_id += 1
            msg["id"] = next_id
        proc.stdin.write((json.dumps(msg) + "\n").encode("utf-8"))
        proc.stdin.flush()
        if notify:
            return None
        while True:
            line = proc.stdout.readline()
            if not line:
                raise RuntimeError("server closed stdout; stderr=" + proc.stderr.read().decode("utf-8", "replace"))
            resp = json.loads(line)
            if resp.get("id") == next_id:
                return resp

    init = send("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "smoke", "version": "0"}})
    print("protocol:", init["result"]["protocolVersion"], "server:", init["result"]["serverInfo"])
    send("notifications/initialized", notify=True)

    tools = send("tools/list")["result"]["tools"]
    for t in tools:
        print(f"- {t['name']:22} annotations={t.get('annotations')}")

    env = send("tools/call", {"name": "get_environment", "arguments": {}})["result"]
    body = json.loads(env["content"][0]["text"])
    print("env:", body["game_version"], body["game_language"], "active:", body["active_mod_count"])

    bad = send("tools/call", {"name": "scan_mod", "arguments": {"mod_id": "does.not.exist"}})["result"]
    print("error path isError:", bad.get("isError"), bad["content"][0]["text"][:80])

    if mod:
        batch = send("tools/call", {"name": "get_batch", "arguments": {"mod_id": mod, "max_items": 3}})["result"]
        b = json.loads(batch["content"][0]["text"])
        print("batch:", b["batch_id"], "items:", len(b["items"]), "remaining:", b["remaining"])
        sub = send("tools/call", {"name": "submit_translations", "arguments": {"batch_id": b["batch_id"], "items": [{"id": "x" * 26, "text": "測試"}]}})["result"]
        print("submit unknown id:", sub["content"][0]["text"][:200])

    proc.stdin.close()
    proc.wait(timeout=30)
    return 0


if __name__ == "__main__":
    sys.exit(main())
