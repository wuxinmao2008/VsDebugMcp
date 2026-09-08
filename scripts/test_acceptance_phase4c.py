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
    print("  Visual Studio MCP Phase 4C: 高级调试控制深化 在线实测            ")
    print("==================================================================")

    # 1. vs_health & vs_capabilities
    print("\n[1/6] 健康检查与 Phase 4C 新能力发现")
    health = call_tool("vs_health")
    inst = health.get("selectedInstance", {})
    vs_id = inst.get("vsInstanceId")
    sln_name = inst.get("solutionName")
    print(f"  vs_health: status={health.get('status')}, vsInstanceId={vs_id}, solution={sln_name}")
    assert health.get("status") == "ok", f"Health status not ok: {health}"

    caps = call_tool("vs_capabilities")
    cap_names = {c.get("name"): c.get("isStub") for c in caps.get("capabilities", [])}
    print(f"  总注册能力数: {len(cap_names)}")
    expected_new_caps = [
        "vs_debugger_freeze_thread",
        "vs_debugger_thaw_thread",
        "vs_debugger_set_next_statement"
    ]
    for cap in expected_new_caps:
        present = cap in cap_names
        is_stub = cap_names.get(cap, True)
        print(f"    * {cap}: present={present}, isStub={is_stub}")
        assert present and not is_stub, f"Capability {cap} missing or stubbed"

    # 2. 准备断点并启动调试至中断状态
    print("\n[2/6] 设置断点并启动 F5 调试 (vs_debugger_start)")
    dbg_info = call_tool("vs_debugger_get_info")
    if dbg_info.get("isDebugging"):
        print("  正在停止残留调试器...")
        call_tool("vs_debugger_stop")
        time.sleep(2)

    prog_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "sample", "SampleApp", "Program.cs"))
    print(f"  下断点: {prog_path}:7")
    bp_res = call_tool("vs_debugger_set_breakpoints", {
        "filePath": prog_path,
        "line": 7,
        "clearExisting": True
    })
    print(f"  断点设置响应: count={len(bp_res.get('breakpoints', []))}")

    print("  启动调试并等待断点着陆...")
    start_res = call_tool("vs_debugger_start", {
        "waitForBreak": True,
        "timeoutSeconds": 15
    })
    mode = start_res.get("currentMode") or start_res.get("mode")
    print(f"  调试模式: {mode}, breakReason={start_res.get('breakReason')}")
    assert mode == "break", f"Expected break mode, got: {mode}"

    # 3. 线程快照与 isFrozen 回显
    print("\n[3/6] 线程快照与冻结状态回显 (vs_debugger_get_threads)")
    threads_res = call_tool("vs_debugger_get_threads")
    threads = threads_res.get("threads", [])
    print(f"  当前线程总数: {len(threads)}, 活动线程 ID: {threads_res.get('currentThreadId')}")
    assert len(threads) > 0, "No threads found"
    target_thread = threads[0]
    target_thread_id = target_thread.get("id")
    print(f"  目标测试线程: ID={target_thread_id}, Name='{target_thread.get('name')}', isFrozen={target_thread.get('isFrozen')}, suspendedCount={target_thread.get('suspendedCount')}")
    assert "isFrozen" in target_thread, "Expected 'isFrozen' field in thread info"

    # 4. 线程挂起与解冻测试 (Freeze & Thaw)
    print(f"\n[4/6] 线程挂起与恢复测试 (vs_debugger_freeze_thread & vs_debugger_thaw_thread, Thread {target_thread_id})")
    print(f"  正在挂起线程 {target_thread_id}...")
    freeze_res = call_tool("vs_debugger_freeze_thread", {"threadId": target_thread_id})
    print(f"  Freeze 响应: {freeze_res}")
    assert not freeze_res.get("isError"), f"Freeze thread failed: {freeze_res}"
    assert freeze_res.get("isFrozen") is True, f"Expected isFrozen=true, got {freeze_res.get('isFrozen')}"
    assert freeze_res.get("action") == "freeze"
    print("    * 成功将目标线程挂起 (isFrozen: True)")

    print(f"  正在恢复/解冻线程 {target_thread_id}...")
    thaw_res = call_tool("vs_debugger_thaw_thread", {"threadId": target_thread_id})
    print(f"  Thaw 响应: {thaw_res}")
    assert not thaw_res.get("isError"), f"Thaw thread failed: {thaw_res}"
    assert thaw_res.get("action") == "thaw"
    print("    * 成功将目标线程解冻恢复")

    # 5. 设置下一语句测试 (vs_debugger_set_next_statement)
    print("\n[5/6] 动态跳转程序计数器 (vs_debugger_set_next_statement)")
    # Currently broken at Program.cs line 7. Move instruction pointer to line 10!
    print("  将当前执行点从 Line 7 移动到 Line 10 (同方法内跳转)...")
    jump_res = call_tool("vs_debugger_set_next_statement", {
        "filePath": "SampleApp/Program.cs",
        "line": 10,
        "column": 1
    })
    print(f"  SetNextStatement 响应: {jump_res}")
    assert not jump_res.get("isError"), f"SetNextStatement failed: {jump_res}"
    assert jump_res.get("success") is True, "Expected success: true"
    assert jump_res.get("line") == 10, f"Expected line 10, got {jump_res.get('line')}"
    top_frame = jump_res.get("topFrame", {})
    print(f"    * 执行点已动态停靠在最新栈帧: line={top_frame.get('lineNumber')}, function={top_frame.get('functionName')}")

    # 6. 逆向防卫测试
    print("\n[6/6] 逆向异常拦截防卫测试")
    # 6.1 Invalid thread ID
    print("  6.1 测试挂起不存在的线程 ID (9999999)...")
    bad_thread_res = call_tool("vs_debugger_freeze_thread", {"threadId": 9999999})
    print(f"      返回: {bad_thread_res}")
    err_str = str(bad_thread_res).lower()
    assert "thread_not_found" in err_str or bad_thread_res.get("isError") is True, f"Expected thread_not_found, got {bad_thread_res}"
    print("      [PASS] 成功拦截无效线程 ID，返回 thread_not_found")

    # 6.2 Set next statement across methods (Calculator.cs:7 while in Program.cs)
    print("  6.2 测试跨方法/跨作用域非法跳转 (跳转至 Calculator.cs:7)...")
    bad_jump_res = call_tool("vs_debugger_set_next_statement", {
        "filePath": "SampleApp/Services/Calculator.cs",
        "line": 7
    })
    print(f"      返回: {bad_jump_res}")
    err_jump_str = str(bad_jump_res).lower()
    assert "invalid_next_statement" in err_jump_str or bad_jump_res.get("isError") is True, f"Expected invalid_next_statement, got {bad_jump_res}"
    print("      [PASS] 成功拦截跨方法非法跳转，返回 invalid_next_statement")

    # 6.3 Set next statement on non-existent file
    print("  6.3 测试对不存在文件设置下一语句...")
    bad_file_res = call_tool("vs_debugger_set_next_statement", {
        "filePath": "SampleApp/NoSuchFile.cs",
        "line": 10
    })
    print(f"      返回: {bad_file_res}")
    err_file_str = str(bad_file_res).lower()
    assert "file_not_found" in err_file_str or bad_file_res.get("isError") is True, f"Expected file_not_found, got {bad_file_res}"
    print("      [PASS] 成功拦截无效文件路径，返回 file_not_found")

    # Cleanup
    print("\n[收尾] 停止调试器...")
    call_tool("vs_debugger_stop")
    time.sleep(1)

    print("\n==================================================================")
    print("  Phase 4C 全部 6 项在线实测验证 100% 通过! (PASS)                 ")
    print("==================================================================")

if __name__ == "__main__":
    main()
