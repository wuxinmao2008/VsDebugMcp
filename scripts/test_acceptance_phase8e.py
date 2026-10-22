import urllib.request
import json
import time
import sys
import os

def call_tool(name, args=None):
    if args is None:
        args = {}
    payload_dict = {
        "jsonrpc": "2.0",
        "id": "1",
        "method": "tools/call",
        "params": {
            "name": name,
            "arguments": args
        }
    }
    req = urllib.request.Request(
        "http://127.0.0.1:43260/",
        data=json.dumps(payload_dict).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream"
        }
    )
    try:
        r = urllib.request.urlopen(req, timeout=60)
        raw = r.read().decode("utf-8")
    except Exception as e:
        return {"error": f"HTTP request failed: {e}"}

    for line in raw.splitlines():
        if line.startswith("data: "):
            try:
                payload = json.loads(line[6:])
                if "result" in payload:
                    res = payload["result"]
                    if "structuredContent" in res:
                        return res["structuredContent"]
                    if "content" in res and res["content"]:
                        txt = res["content"][0].get("text", "")
                        try:
                            return json.loads(txt)
                        except Exception:
                            return {"isError": res.get("isError", False), "text": txt}
                elif "error" in payload:
                    return payload["error"]
            except Exception as e:
                return {"error": f"JSON parse error: {e}"}
    return {"raw": raw}

def main():
    print("==================================================")
    print("   VsDebugMcp Phase 8E: Thread Context & Deadlock Analysis Acceptance")
    print("==================================================")

    # 1. Health check
    print("\n[Step 1] Health and Instance Verification (vs_health)")
    health = call_tool("vs_health")
    status = health.get("status")
    host_ver = health.get("hostVersion")
    bridge_ver = health.get("bridgeVersion")
    selected_inst = health.get("selectedInstance", {})
    vs_id = selected_inst.get("vsInstanceId")
    sol_name = selected_inst.get("solutionName")
    print(f"  Health status:  {status}")
    print(f"  Host Version:   {host_ver}")
    print(f"  Bridge Version: {bridge_ver}")
    print(f"  Instance ID:    {vs_id}")
    print(f"  Solution Name:  {sol_name}")

    # 2. Capabilities check
    print("\n[Step 2] Capabilities Check (vs_capabilities)")
    caps = call_tool("vs_capabilities")
    tools = caps.get("capabilities", [])
    tool_names = [t.get("name") for t in tools]
    print(f"  Discovered {len(tools)} capabilities on instance.")

    new_phase8e_tools = [
        "vs_debugger_switch_thread",
        "vs_debugger_get_all_threads_call_stacks",
        "vs_debugger_read_memory_typed"
    ]
    for nt in new_phase8e_tools:
        assert nt in tool_names, f"Expected Phase 8E tool '{nt}' not found in capabilities!"
        print(f"  [PASS] Tool discovered: {nt}")

    # 3. All threads call stack & Deadlock heuristics
    print("\n[Step 3] All Threads Call Stack Overview (~*k equivalent)")
    all_threads = call_tool("vs_debugger_get_all_threads_call_stacks", {"maxFramesPerThread": 3})
    total_threads = all_threads.get("totalThreads", 0)
    suspect_count = all_threads.get("suspectThreadsCount", 0)
    threads = all_threads.get("threads", [])
    print(f"  Total threads captured: {total_threads}")
    print(f"  Deadlock suspect threads: {suspect_count}")

    suspect_tid = None
    for t in threads:
        tid = t.get("threadId")
        name = t.get("threadName", "")
        is_suspect = t.get("isDeadlockSuspect", False)
        heuristic = t.get("waitingHeuristic")
        top_frames = t.get("topFrames", [])
        if is_suspect:
            suspect_tid = tid
            top_fn = top_frames[0].get("functionName") if top_frames else "N/A"
            print(f"  -> Suspect TID {tid} ('{name}'): Heuristic={heuristic}, TopFrame={top_fn}")

    # 4. Thread switching
    if suspect_tid:
        print(f"\n[Step 4] Switch Thread Context to Suspect Thread (TID {suspect_tid})")
        switch_res = call_tool("vs_debugger_switch_thread", {"threadId": suspect_tid})
        print(f"  Switch result: {switch_res}")
        assert switch_res.get("success") is True, f"Failed to switch to thread {suspect_tid}"

    # 5. Typed memory reading
    print("\n[Step 5] Typed Memory Reading (vs_debugger_read_memory_typed)")
    info = call_tool("vs_debugger_get_info")
    mode = info.get("mode")
    print(f"  Debugger mode: {mode}")

    if mode == "break":
        # Test reading typed dword
        mem_dword = call_tool("vs_debugger_read_memory_typed", {
            "address": "0x28EC8348",
            "type": "dword",
            "count": 2
        })
        print(f"  Typed DWORD read response: {mem_dword}")

        # Test reading ascii string
        mem_ascii = call_tool("vs_debugger_read_memory_typed", {
            "address": "0x28EC8348",
            "type": "ascii_string",
            "count": 16
        })
        print(f"  Typed ASCII string read response: {mem_ascii}")

    print("\n==================================================")
    print("   Phase 8E Acceptance Verification Complete!")
    print("==================================================")

if __name__ == "__main__":
    main()
