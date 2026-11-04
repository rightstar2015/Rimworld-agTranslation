"""經由 MCP stdio 呼叫單一工具並印出結果（測試用）。

用法：python -I mcp_call.py <rwat.exe> <工具名稱> <參數 JSON 檔或 JSON 字串>
"""
import json
import os
import subprocess
import sys


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    exe, tool, raw = sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "{}"
    args = json.load(open(raw, encoding="utf-8")) if os.path.isfile(raw) else json.loads(raw)
    proc = subprocess.Popen([exe, "serve"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

    def rpc(msg):
        proc.stdin.write((json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8"))
        proc.stdin.flush()
        if "id" not in msg:
            return None
        while True:
            resp = json.loads(proc.stdout.readline())
            if resp.get("id") == msg["id"]:
                return resp

    rpc({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "call", "version": "0"}}})
    rpc({"jsonrpc": "2.0", "method": "notifications/initialized"})
    result = rpc({"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": tool, "arguments": args}})["result"]
    if result.get("isError"):
        print("ERROR:", result["content"][0]["text"])
    else:
        print(result["content"][0]["text"])
    proc.stdin.close()
    proc.wait(timeout=30)
    return 1 if result.get("isError") else 0


if __name__ == "__main__":
    sys.exit(main())
