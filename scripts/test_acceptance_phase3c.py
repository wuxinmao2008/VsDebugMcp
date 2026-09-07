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

import subprocess

def main():
    print("==================================================================")
    print("  Visual Studio MCP Phase 3C: 进程附加与模块符号诊断 在线实测   ")
    print("==================================================================")

    # 1. vs_health & vs_capabilities
    print("\n[1/6] 健康检查与 Phase 3C 新能力发现")
    health = call_tool("vs_health")
    inst = health.get("selectedInstance", {})
    print(f"  vs_health: status={health.get('status')}, vsInstanceId={inst.get('vsInstanceId')}, solution={inst.get('solutionName')}")
    assert health.get("status") == "ok", f"Health status not ok: {health}"

    caps = call_tool("vs_capabilities")
    cap_names = {c.get("name"): c.get("isStub") for c in caps.get("capabilities", [])}
    print(f"  总注册能力数: {len(cap_names)}")
    expected_new_caps = [
        "vs_debugger_get_processes",
        "vs_debugger_attach_process",
        "vs_debugger_detach",
        "vs_debugger_get_modules"
    ]
    for cap in expected_new_caps:
        present = cap in cap_names
        is_stub = cap_names.get(cap, True)
        print(f"    * {cap}: present={present}, isStub={is_stub}")
        assert present and not is_stub, f"Capability {cap} missing or stubbed"

    # 2. vs_debugger_get_processes (系统运行进程检索与过滤)
    print("\n[2/6] 系统运行进程查询与过滤 (vs_debugger_get_processes)")
    procs_res = call_tool("vs_debugger_get_processes", {"maxCount": 20})
    total = procs_res.get("totalCount", 0)
    print(f"  查询前 20 个本地进程: 返回数={total}")
    assert total > 0, "No processes returned"
    sample_proc = procs_res.get("processes", [])[0]
    print(f"    * 样本进程: PID={sample_proc.get('processId')}, Name={sample_proc.get('name')}, User={sample_proc.get('userName')}")

    # 3. 启动常驻测试目标进程
    print("\n[3/6] 启动外部靶场常驻目标进程")
    target_cmd = ["powershell", "-NoProfile", "-Command", "Start-Sleep -Seconds 120"]
    target_proc = subprocess.Popen(target_cmd)
    target_pid = target_proc.pid
    print(f"  已启动常驻目标进程: PID={target_pid}")
    time.sleep(1)

    try:
        # 查询特定 PID
        pid_query = call_tool("vs_debugger_get_processes", {"processId": target_pid})
        matched_procs = pid_query.get("processes", [])
        print(f"  按 PID={target_pid} 精确检索结果: count={len(matched_procs)}")
        assert len(matched_procs) == 1, f"Expected 1 matched process, got {len(matched_procs)}"
        print(f"    * 成功锁定目标进程: PID={matched_procs[0].get('processId')}, Name={matched_procs[0].get('name')}")

        # 4. vs_debugger_attach_process (附加到进程)
        print(f"\n[4/6] 附加 Visual Studio 调试器到外部进程 (PID={target_pid})")
        attach_res = call_tool("vs_debugger_attach_process", {
            "processId": target_pid,
            "waitForBreak": False
        })
        print(f"  附加响应: ProcessId={attach_res.get('processId')}, Mode={attach_res.get('currentMode')}, IsDebugging={attach_res.get('isDebugging')}")
        assert attach_res.get("isDebugging") is True, f"Debugger is not debugging after attach: {attach_res}"

        time.sleep(1.5)

        # 5. vs_debugger_get_modules (提取加载模块与符号信息)
        print("\n[5/6] 提取目标进程已加载模块与符号状态 (vs_debugger_get_modules)")
        modules_res = call_tool("vs_debugger_get_modules", {
            "processId": target_pid,
            "maxCount": 30
        })
        mods = modules_res.get("modules", [])
        print(f"  获取已加载模块数: totalCount={modules_res.get('totalCount')}, returnedCount={len(mods)}")
        assert len(mods) > 0, "No modules returned from attached process"
        for m in mods[:5]:
            print(f"    * 模块: {m.get('name')} | 加载基址: {m.get('loadAddress')} | 符号: {m.get('symbolFile')} (Loaded: {m.get('symbolsLoaded')})")

        # 验证暂停
        print("\n  尝试暂停目标进程 (vs_debugger_pause)")
        pause_res = call_tool("vs_debugger_pause", {"waitForBreak": True})
        print(f"  暂停状态: Mode={pause_res.get('currentMode')}, Reason={pause_res.get('lastBreakReason')}")

        # 6. vs_debugger_detach (安全分离调试器，验证进程存活)
        print(f"\n[6/6] 安全分离调试器 (vs_debugger_detach, PID={target_pid})")
        detach_res = call_tool("vs_debugger_detach", {"processId": target_pid})
        print(f"  分离响应: Mode={detach_res.get('currentMode')}, IsDebugging={detach_res.get('isDebugging')}")
        assert detach_res.get("currentMode") == "design", f"Debugger not back in design mode: {detach_res}"

        # 验证外部目标进程依然存活
        poll_ret = target_proc.poll()
        print(f"  验证外部目标进程存活状态: poll={poll_ret} (None 表示继续健康存活)")
        assert poll_ret is None, "Target process was unexpectedly terminated upon detach!"
        print("  目标进程在分离后继续在操作系统中独立运行，安全分离验证通过！")

    finally:
        print("\n[收尾] 清理外部常驻测试目标进程")
        try:
            target_proc.terminate()
            target_proc.wait(timeout=3)
        except Exception:
            pass
        print("  外部测试目标进程已安全清理。")

    print("\n==================================================================")
    print("  Phase 3C 全部验收测试顺利通过！(100% PASS)                     ")
    print("==================================================================")

if __name__ == "__main__":
    main()
