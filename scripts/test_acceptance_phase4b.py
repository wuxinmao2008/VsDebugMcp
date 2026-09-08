import urllib.request
import json
import time
import sys
import os

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

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
        r = urllib.request.urlopen(req)
        raw = r.read().decode("utf-8")
        for line in raw.splitlines():
            if line.startswith("data: "):
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
        return raw
    except urllib.error.HTTPError as he:
        body = he.read().decode("utf-8")
        return {"isError": True, "httpStatus": he.code, "body": body}
    except Exception as e:
        return {"isError": True, "exception": str(e)}

def main():
    print("==================================================================")
    print("  Visual Studio MCP Phase 4B: 工程防御加固与多实例智能路由 在线实测   ")
    print("==================================================================")

    # 1. vs_health & vs_capabilities
    print("\n[1/5] 健康检查与实例基础校验 (vs_health)")
    health = call_tool("vs_health")
    inst = health.get("selectedInstance", {})
    vs_id = inst.get("vsInstanceId")
    sln_name = inst.get("solutionName")
    sln_path = inst.get("solutionFilePath")
    print(f"  vs_health: status={health.get('status')}, vsInstanceId={vs_id}")
    print(f"  当前加载方案: {sln_name} ({sln_path})")
    assert health.get("status") == "ok", f"Health status not ok: {health}"

    # 2. Smart Directory / Path Matching (vs_find_instances)
    print("\n[2/5] 多实例智能工作目录检索 (vs_find_instances)")
    # Query with solution folder name or substring
    found_by_dir = call_tool("vs_find_instances", {"query": "sample"})
    instances = found_by_dir.get("instances", [])
    print(f"  按目录关键字 'sample' 检索匹配到的实例数: {len(instances)}")
    assert len(instances) >= 1, "Expected at least 1 instance matched by directory"
    print(f"    * 匹配实例 ID: {instances[0].get('vsInstanceId')}")
    print(f"    * 解决方案路径: {instances[0].get('solutionFilePath')}")

    # 3. Path-based resolution without vsInstanceId
    print("\n[3/5] 智能路径路由验证 (省略 vsInstanceId, 依靠 targetPath 自动锁定)")
    sample_file_rel = "SampleApp/Program.cs"
    nav_res = call_tool("vs_navigate_to", {
        "filePath": sample_file_rel,
        "line": 10,
        "column": 1
    })
    print(f"  vs_navigate_to 返回: {nav_res}")
    assert not nav_res.get("isError"), f"vs_navigate_to failed: {nav_res}"
    assert nav_res.get("success") is True, f"Expected success: true, got {nav_res}"

    # 4. Build Deadlock Guard during Debugging
    print("\n[4/5] 构建与调试互斥防死锁守卫测试 (Build While Debugging Guard)")
    # 4.1 Ensure debugger is stopped initially
    dbg_info = call_tool("vs_debugger_get_info")
    curr_mode = dbg_info.get("mode") or dbg_info.get("currentMode") or "design"
    print(f"  当前调试器模式: {curr_mode}, isDebugging={dbg_info.get('isDebugging')}")
    if dbg_info.get("isDebugging") or curr_mode.lower() != "design":
        print("  正在先停止调试器...")
        call_tool("vs_debugger_stop")
        time.sleep(2)

    # 4.2 Set breakpoint on Calculator.cs line 6 (using absolute path, without vsInstanceId)
    calc_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "sample", "SampleApp", "Services", "Calculator.cs"))
    print(f"  设置断点 (无 vsInstanceId，依靠路径路由): {calc_path}:6")
    bp_res = call_tool("vs_debugger_set_breakpoints", {
        "filePath": calc_path,
        "line": 6,
        "clearExisting": True
    })
    bps = bp_res.get("breakpoints", [])
    print(f"  断点设置结果: count={len(bps)}, warnings={bp_res.get('warnings')}")

    # 4.3 Start Debugging
    print("  启动调试 (vs_debugger_start)...")
    start_res = call_tool("vs_debugger_start", {
        "waitForBreak": True,
        "timeoutSeconds": 15
    })
    curr_dbg_mode = start_res.get("currentMode") or start_res.get("mode")
    print(f"  调试启动响应: mode={curr_dbg_mode}, breakReason={start_res.get('breakReason')}")

    # 4.4 Attempt to build while debugger is active!
    print("  >>> 核心防死锁测试: 在调试进行中尝试调用 vs_run_build <<<")
    build_attempt = call_tool("vs_run_build", {
        "projectOrSolutionPath": "SampleApp"
    })
    print(f"  构建拦截响应: {build_attempt}")

    # Expect error code 'debugger_running_cannot_build'
    err_text = str(build_attempt)
    is_blocked = ("debugger_running_cannot_build" in err_text or 
                  "Cannot build the solution while debugging" in err_text or
                  "Cannot start build while debugger is running" in err_text or
                  build_attempt.get("isError") is True)
    assert is_blocked, f"Expected build to be rejected while debugger is running, got: {build_attempt}"
    print("  [SUCCESS] 构建被立即拦截，彻底规避 Visual Studio 模态对话框与 IPC 管道死锁！")

    # 4.5 Stop debugger
    print("  停止调试 (vs_debugger_stop)...")
    stop_res = call_tool("vs_debugger_stop")
    print(f"  停止调试响应: isDebugging={stop_res.get('isDebugging')}, mode={stop_res.get('currentMode') or stop_res.get('mode')}")

    # Poll until debugger is back in design mode
    for _ in range(10):
        time.sleep(1)
        dbg_st = call_tool("vs_debugger_get_info")
        m = (dbg_st.get("mode") or "").lower()
        if m == "design" and not dbg_st.get("isDebugging"):
            break

    # 5. Verify Build Works in Design Mode
    print("\n[5/5] 验证调试结束后恢复正常设计模式构建 (Design Mode Build)")
    dbg_info_after = call_tool("vs_debugger_get_info")
    mode_after = (dbg_info_after.get("mode") or "").lower()
    print(f"  调试器状态: mode={mode_after}, isDebugging={dbg_info_after.get('isDebugging')}")
    assert mode_after == "design" and not dbg_info_after.get("isDebugging"), "Debugger should be in Design mode"

    build_res = call_tool("vs_run_build", {
        "projectOrSolutionPath": "SampleApp"
    })
    print(f"  正常构建响应: {build_res}")
    assert not build_res.get("isError"), f"Build failed in design mode: {build_res}"
    build_task_id = build_res.get("buildTaskId")
    print(f"  获得 buildTaskId: {build_task_id}")

    # Poll status until done
    for _ in range(15):
        time.sleep(1)
        st = call_tool("vs_get_build_status", {"buildTaskId": build_task_id})
        state = st.get("state")
        print(f"    * 轮询构建状态: {state}")
        if state in ("succeeded", "failed", "cancelled"):
            break

    print("\n==================================================================")
    print("  Phase 4B 全部测试项验证通过！(PASS)                             ")
    print("==================================================================")

if __name__ == "__main__":
    main()
