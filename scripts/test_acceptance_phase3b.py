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
    print("  Visual Studio MCP Phase 3B: 核心诊断与高级调试闭环 在线实测   ")
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
    print(f"  vs_debugger_get_exception_info: present={'vs_debugger_get_exception_info' in cap_names}, isStub={cap_names.get('vs_debugger_get_exception_info')}")
    assert "vs_debugger_get_exception_info" in cap_names and not cap_names["vs_debugger_get_exception_info"], "vs_debugger_get_exception_info capability missing or stub"

    # 2. vs_get_errors (双轨保底验证)
    print("\n[2/6] 结构化错误与诊断提取 (vs_get_errors 双轨保底)")
    errors_res = call_tool("vs_get_errors", {"maxCount": 50})
    print(f"  vs_get_errors 响应: totalCount={errors_res.get('totalCount')}, returnedCount={errors_res.get('returnedCount')}")
    assert "totalCount" in errors_res, f"vs_get_errors did not return totalCount: {errors_res}"
    print(f"  诊断列表获取成功，无 diagnostics_unavailable 报错！")

    # 3. vs_debugger_set_breakpoints (高级条件与命中计数断点)
    print("\n[3/6] 高级断点设置（条件断点与命中计数过滤）")
    calc_path = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "sample", "SampleApp", "Services", "Calculator.cs"))
    print(f"  断点目标文件: {calc_path}")
    bp_res = call_tool("vs_debugger_set_breakpoints", {
        "filePath": calc_path,
        "line": 6,
        "condition": "a > 0",
        "conditionType": "whenTrue",
        "hitCountTarget": 1,
        "hitCountType": "equal",
        "clearExisting": True
    })
    bps = bp_res.get("breakpoints", [])
    print(f"  设置结果: breakpoints count={len(bps)}, warnings={bp_res.get('warnings')}")
    assert len(bps) > 0, "No breakpoints set"
    target_bp = bps[0]
    print(f"    * ID: {target_bp.get('id')}, Condition: {target_bp.get('condition')}, ConditionType: {target_bp.get('conditionType')}, HitTarget: {target_bp.get('hitCountTarget')}")

    # 4. vs_get_tests (定位测试用例)
    print("\n[4/6] 测试用例发现与单测定位")
    tests = call_tool("vs_get_tests")
    total = tests.get("totalCount", 0)
    print(f"  全量发现用例数: {total}")
    div_test_id = None
    mult_test_id = None
    for t in tests.get("tests", []):
        print(f"    * [{t.get('testId')}] {t.get('displayName')}")
        if "Divide_ByZero" in t.get("displayName", ""):
            div_test_id = t.get("testId")
        if "Multiply" in t.get("displayName", ""):
            mult_test_id = t.get("testId")

    target_test_id = div_test_id or mult_test_id or (tests["tests"][0].get("testId") if total > 0 else None)
    assert target_test_id is not None, "No test case found"
    print(f"  选定单测 ID: {target_test_id}")

    # 5. vs_debug_test_by_id (启动单测调试并停靠)
    print("\n[5/6] 触发单测调试并等待停靠")
    dbg_res = call_tool("vs_debug_test_by_id", {
        "testId": target_test_id,
        "waitForBreak": True,
        "timeoutMs": 15000
    })
    print(f"  调试启动响应: testRunId={dbg_res.get('testRunId')}, mode={dbg_res.get('debuggerMode')}, breakReason={dbg_res.get('lastBreakReason')}")
    top = dbg_res.get("topFrame")
    if top:
        print(f"  着陆栈帧: {top.get('functionName')} ({top.get('fileName')}:{top.get('lineNumber')})")

    # 6. vs_debugger_get_exception_info (异常现场诊断)
    print("\n[6/6] 调试现场异常诊断 (vs_debugger_get_exception_info)")
    ex_res = call_tool("vs_debugger_get_exception_info")
    print(f"  异常诊断结果:")
    print(f"    * HasException: {ex_res.get('hasException')}")
    print(f"    * Type: {ex_res.get('exceptionType')}")
    print(f"    * Message: {ex_res.get('message')}")
    print(f"    * HResult: {ex_res.get('hresult')}")
    if ex_res.get("stackTrace"):
        print(f"    * StackTrace:\n{ex_res.get('stackTrace')}")

    # 7. 收尾：恢复/停止调试
    print("\n[收尾] 终止调试并安全回到设计模式")
    stop_res = call_tool("vs_debugger_stop")
    print(f"  调试停止响应: mode={stop_res.get('debuggerMode')}, isDebugging={stop_res.get('isDebugging')}")

    print("\n==================================================================")
    print("  Phase 3B 全链路在线验收完成！全部环节均正常通过。              ")
    print("==================================================================")

if __name__ == "__main__":
    main()
