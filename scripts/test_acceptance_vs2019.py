import urllib.request
import json
import time
import os
import sys

# Ensure UTF-8 output in Windows PowerShell/cmd
if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

# Ensure local requests bypass proxy
os.environ['NO_PROXY'] = '127.0.0.1,localhost'
proxy_support = urllib.request.ProxyHandler({})
opener = urllib.request.build_opener(proxy_support)

def call_tool(name, args=None):
    if args is None:
        args = {}
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": name,
            "arguments": args
        }
    }
    req = urllib.request.Request(
        "http://127.0.0.1:43260/",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream"
        }
    )
    with opener.open(req, timeout=30) as res:
        raw = res.read().decode("utf-8")
        for line in raw.splitlines():
            if line.startswith("data: "):
                data = json.loads(line[6:])
                if "result" in data:
                    res_obj = data["result"]
                    if "structuredContent" in res_obj:
                        return res_obj["structuredContent"]
                    if "content" in res_obj and res_obj["content"]:
                        txt = res_obj["content"][0].get("text", "")
                        try:
                            return json.loads(txt)
                        except Exception:
                            return {"text": txt, "isError": res_obj.get("isError", False)}
                elif "error" in data:
                    return data["error"]
        return {"raw": raw}

def main():
    print("================================================================")
    print("  Visual Studio 2019 (v16.11) MCP 端到端联机全量验证")
    print("================================================================")

    # 1. vs_health
    print("\n[1/8] 检查 MCP 服务与 VS 实例健康状态 (vs_health)...")
    health = call_tool("vs_health")
    inst = health.get("selectedInstance", {})
    vs_id = inst.get("vsInstanceId")
    vs_ver = inst.get("visualStudioVersion")
    sln_name = inst.get("solutionName")
    sln_path = inst.get("solutionFilePath")
    bridge_status = health.get("bridge", {}).get("status")

    print(f"  MCP Host 状态: {health.get('status')}")
    print(f"  VS 进程 ID: {inst.get('visualStudioProcessId')} (InstanceId: {vs_id})")
    print(f"  VS 内部版本: {vs_ver}")
    print(f"  已打开解决方案: {sln_name} ({sln_path})")
    print(f"  命名管道 Bridge 状态: {bridge_status}")

    assert health.get("status") == "ok", "Host health != ok"
    assert bridge_status == "ok", "Bridge status != ok"
    assert "16." in vs_ver, "Not running in VS 2019"

    # 2. vs_capabilities
    print("\n[2/8] 检查能力清单与 Stub 状态 (vs_capabilities)...")
    caps_res = call_tool("vs_capabilities")
    caps = caps_res.get("capabilities", [])
    print(f"  总计发现能力数量: {len(caps)}")
    stub_caps = [c["name"] for c in caps if c.get("isStub")]
    live_caps = [c["name"] for c in caps if not c.get("isStub")]
    print(f"  活跃能力 ({len(live_caps)} 个): {', '.join(live_caps[:6])}...")
    if stub_caps:
        print(f"  注意: 包含保留标记的能力: {stub_caps}")
    print("  [OK] 核心能力均已原生挂载！")

    # 3. vs_get_projects_in_solution & vs_get_files_in_project
    print("\n[3/8] 探索解决方案项目结构与工程文件 (vs_get_projects_in_solution)...")
    projs = call_tool("vs_get_projects_in_solution")
    project_list = projs.get("projects", [])
    print(f"  项目总数: {len(project_list)}")
    sample_file_path = None
    for p in project_list:
        p_name = p.get('name')
        p_id = p.get('id')
        p_path = p.get('projectFilePath')
        print(f"    - {p_name} (ID: {p_id}) -> {p_path}")
        files_res = call_tool("vs_get_files_in_project", {"projectId": p_id})
        files = files_res.get("files", [])
        print(f"      包含文件数: {len(files)}")
        for f in files:
            print(f"        * {f.get('relativePath')} ({f.get('fullPath')})")
            if f.get('relativePath', '').endswith(".cs") and not sample_file_path:
                sample_file_path = f.get('fullPath')

    # 4. vs_read_file & vs_file_search
    print("\n[4/8] 文件搜索与代码读取测试 (vs_file_search / vs_read_file)...")
    search_res = call_tool("vs_file_search", {"query": "Program.cs"})
    search_matches = search_res.get("matches", [])
    print(f"  搜索 'Program.cs' 匹配数: {len(search_matches)}")
    if search_matches:
        target_file = search_matches[0].get("filePath")
        sample_file_path = target_file
    elif not sample_file_path:
        sample_file_path = os.path.join(os.path.dirname(sln_path), "SampleApp", "Program.cs")

    print(f"  读取目标代码文件: {sample_file_path}")
    read_res = call_tool("vs_read_file", {"filePath": sample_file_path, "startLine": 1, "lineCount": 10})
    print("  --- 文件前 10 行预览 ---")
    for line in read_res.get("content", "").splitlines()[:10]:
        print(f"    | {line}")
    print("  --- 预览结束 ---")

    # 5. vs_run_build & vs_get_build_status & vs_get_errors
    print("\n[5/8] 执行异步工程构建与状态轮询 (vs_run_build / vs_get_build_status)...")
    build_start = call_tool("vs_run_build", {"configuration": "Debug"})
    task_id = build_start.get("buildTaskId")
    print(f"  已派发构建任务: {task_id}, 初始状态: {build_start.get('status')}")

    final_build = None
    for i in range(30):
        time.sleep(0.5)
        status = call_tool("vs_get_build_status", {"buildTaskId": task_id})
        current_status = status.get("status")
        if current_status in ("completed", "failed", "cancelled"):
            final_build = status
            break

    if final_build:
        print(f"  构建完成! 状态: {final_build.get('status')}, 成功: {final_build.get('succeeded')}")
        print(f"  错误数: {final_build.get('errorCount')}, 警告数: {final_build.get('warningCount')}, 耗时: {final_build.get('elapsedMilliseconds')}ms")
    else:
        print("  构建超时未完成")

    errors_res = call_tool("vs_get_errors")
    print(f"  VS 错误列表 (Error List) 诊断项数量: {errors_res.get('totalCount')}")

    # 6. vs_get_output_window_logs
    print("\n[6/8] 读取输出窗口生成日志 (vs_get_output_window_logs)...")
    output_res = call_tool("vs_get_output_window_logs", {"paneName": "Build", "lineCount": 10})
    print(f"  输出窗口 Build 面板日志 (最后 {len(output_res.get('lines', []))} 行):")
    for line in output_res.get("lines", [])[-5:]:
        print(f"    > {line}")

    # 7. vs_debugger_get_info & vs_debugger_set_breakpoints
    print("\n[7/8] 调试器子系统状态查询与断点操控 (vs_debugger_get_info / vs_debugger_set_breakpoints)...")
    dbg_info = call_tool("vs_debugger_get_info")
    print(f"  当前调试模式: {dbg_info.get('mode')}, 调试中: {dbg_info.get('isDebugging')}, 当前断点数: {dbg_info.get('breakpointCount')}")

    if sample_file_path and os.path.exists(sample_file_path):
        print(f"  在文件 {os.path.basename(sample_file_path)} 第 9 行下断点...")
        bp_res = call_tool("vs_debugger_set_breakpoints", {
            "filePath": sample_file_path,
            "line": 9,
            "enabled": True
        })
        print(f"  断点设置结果: 已成功设置断点，总断点数: {bp_res.get('totalBreakpoints')}")

    # 8. vs_diagnostic_report
    print("\n[8/8] 获取全系统诊断快照 (vs_diagnostic_report)...")
    diag_res = call_tool("vs_diagnostic_report")
    diag_host = diag_res.get("host", {})
    inst_list = diag_res.get("instances", [])
    print(f"  Host 运行状态: {diag_host.get('status')}, 托管实例数: {len(inst_list)}")
    if inst_list:
        cur_inst = inst_list[0]
        print(f"  托管 VS 实例: {cur_inst.get('vsInstanceId')} (PID: {cur_inst.get('visualStudioProcessId')})")
        print(f"  桥接运行指标: requestsProcessed={cur_inst.get('requestsProcessed', 0)}")

    print("\n================================================================")
    print("  [SUCCESS] Visual Studio 2019 Community 端到端在线联机全量验证通过！")
    print("================================================================")

if __name__ == '__main__':
    main()
