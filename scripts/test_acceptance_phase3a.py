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

def main():
    print("==================================================================")
    print("  Visual Studio MCP Phase 3A: 测试调试联动与多线程诊断 在线实测   ")
    print("==================================================================")

    # 1. vs_health & vs_capabilities
    print("\n[1/6] 健康检查与能力发现")
    health = call_tool("vs_health")
    inst = health.get("selectedInstance", {})
    print(f"  vs_health: status={health.get('status')}, vsInstanceId={inst.get('vsInstanceId')}, solution={inst.get('solutionName')}")
    assert health.get("status") == "ok", f"Health status not ok: {health}"

    caps = call_tool("vs_capabilities")
    cap_names = {c.get("name"): c.get("isStub") for c in caps.get("capabilities", [])}
    print(f"  总注册能力数: {len(cap_names)}")
    print(f"  vs_debug_test_by_id: present={ 'vs_debug_test_by_id' in cap_names }, isStub={cap_names.get('vs_debug_test_by_id')}")
    print(f"  vs_debugger_get_threads: present={ 'vs_debugger_get_threads' in cap_names }, isStub={cap_names.get('vs_debugger_get_threads')}")
    assert "vs_debug_test_by_id" in cap_names and not cap_names["vs_debug_test_by_id"], "vs_debug_test_by_id capability missing or stub"
    assert "vs_debugger_get_threads" in cap_names and not cap_names["vs_debugger_get_threads"], "vs_debugger_get_threads capability missing or stub"

    # 2. vs_get_tests
    print("\n[2/6] 测试用例发现与目标获取")
    tests = call_tool("vs_get_tests")
    total = tests.get("totalCount", 0)
    print(f"  全量发现用例数: {total}")
    multiply_id = None
    multiply_file = None
    multiply_line = None
    for t in tests.get("tests", []):
        print(f"    * [{t.get('testId')}] {t.get('displayName')} (file: {t.get('filePath')}:{t.get('lineNumber')})")
        if "Multiply" in t.get("displayName", ""):
            multiply_id = t.get("testId")
            multiply_file = t.get("filePath")
            multiply_line = t.get("lineNumber")

    assert multiply_id is not None, "Multiply test case not found"
    print(f"  选中目标测试: Multiply (ID: {multiply_id})")

    # 3. vs_debugger_set_breakpoints
    print("\n[3/6] 在被测业务代码设置断点")
    # Calculator.cs is typically in sample/SampleApp/Services/Calculator.cs or SampleLib
    calc_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "sample", "SampleApp", "Services", "Calculator.cs"))
    print(f"  断点目标文件: {calc_path}")
    bp_res = call_tool("vs_debugger_set_breakpoints", {
        "filePath": calc_path,
        "line": 6,
        "clearExisting": True
    })
    print(f"  断点设置结果: breakpoints count={len(bp_res.get('breakpoints', []))}, warnings={bp_res.get('warnings')}")

    test_cs = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "sample", "SampleTests", "CalculatorTests.cs"))
    bp2_res = call_tool("vs_debugger_set_breakpoints", {
        "filePath": test_cs,
        "line": 18,
        "clearExisting": True
    })
    print(f"  测试代码断点结果: count={len(bp2_res.get('breakpoints', []))}")

    # 4. vs_debug_test_by_id
    print(f"\n[4/6] 调用 vs_debug_test_by_id 启动测试调试 (waitForBreak=True)")
    debug_res = call_tool("vs_debug_test_by_id", {
        "testId": multiply_id,
        "waitForBreak": True,
        "timeoutMs": 15000
    })
    print(f"  调试执行响应:")
    print(f"    - testRunId: {debug_res.get('testRunId')}")
    print(f"    - debuggerMode: {debug_res.get('debuggerMode')}")
    print(f"    - isDebugging: {debug_res.get('isDebugging')}")
    print(f"    - lastBreakReason: {debug_res.get('lastBreakReason')}")
    top = debug_res.get("topFrame")
    if top:
        print(f"    - topFrame: {top.get('functionName')} at {top.get('fileName')}:{top.get('lineNumber')}")
    else:
        print(f"    - topFrame: <None>")
    print(f"    - warnings: {debug_res.get('warnings')}")

    # 5. vs_debugger_get_threads & vs_debugger_get_locals
    print("\n[5/6] 断点现场诊断与多线程巡检")
    threads_res = call_tool("vs_debugger_get_threads")
    print(f"  线程总数: {threads_res.get('totalCount')}, 当前活动线程ID: {threads_res.get('currentThreadId')}")
    for th in threads_res.get("threads", [])[:5]:
        cur = " (CURRENT)" if th.get("isCurrent") else ""
        print(f"    - [TID: {th.get('id')}] {th.get('name') or '<unnamed>'}{cur}, Alive={th.get('isAlive')}, Priority={th.get('priority')}")

    locals_res = call_tool("vs_debugger_get_locals")
    print(f"  当前栈帧局部变量与参数 (共 {locals_res.get('totalCount')} 项):")
    for v in locals_res.get("variables", []):
        arg = " (arg)" if v.get("isArgument") else ""
        print(f"    * {v.get('name')} = {v.get('value')} [{v.get('type')}]{arg}")

    # 6. 继续并结束调试
    print("\n[6/6] 继续执行并退出调试会话")
    cont_res = call_tool("vs_debugger_continue", {"waitForBreak": False})
    print(f"  continue 响应: mode={cont_res.get('currentMode')}")
    time.sleep(2)

    info_res = call_tool("vs_debugger_get_info")
    print(f"  最终调试器状态: mode={info_res.get('mode')}, isDebugging={info_res.get('isDebugging')}")
    if info_res.get("isDebugging"):
        call_tool("vs_debugger_stop")
        print("  已调用 stop 强制重置为设计模式")

    print("\n==================================================================")
    print("  Phase 3A 全链路端到端实测验证完成！                             ")
    print("==================================================================")

if __name__ == "__main__":
    main()
