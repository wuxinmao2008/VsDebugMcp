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
    print("  Visual Studio MCP Phase 4A: 活动上下文与编辑器协同导航 在线实测   ")
    print("==================================================================")

    # 1. vs_health & vs_capabilities
    print("\n[1/6] 健康检查与 Phase 4A 新能力发现")
    health = call_tool("vs_health")
    inst = health.get("selectedInstance", {})
    print(f"  vs_health: status={health.get('status')}, vsInstanceId={inst.get('vsInstanceId')}, solution={inst.get('solutionName')}")
    assert health.get("status") == "ok", f"Health status not ok: {health}"

    caps = call_tool("vs_capabilities")
    cap_names = {c.get("name"): c.get("isStub") for c in caps.get("capabilities", [])}
    print(f"  总注册能力数: {len(cap_names)}")
    expected_new_caps = [
        "vs_get_active_document",
        "vs_navigate_to",
        "vs_get_solution_configurations"
    ]
    for cap in expected_new_caps:
        present = cap in cap_names
        is_stub = cap_names.get(cap, True)
        print(f"    * {cap}: present={present}, isStub={is_stub}")
        assert present and not is_stub, f"Capability {cap} missing or stubbed"

    # 2. vs_get_solution_configurations
    print("\n[2/6] 查询解决方案全部构建配置与平台 (vs_get_solution_configurations)")
    sln_configs = call_tool("vs_get_solution_configurations")
    assert not sln_configs.get("isError"), f"vs_get_solution_configurations failed: {sln_configs}"
    sol_name = sln_configs.get("solutionName", "")
    active_cfg = sln_configs.get("activeConfigurationName", "")
    active_platform = sln_configs.get("activePlatformName", "")
    configs = sln_configs.get("configurations", [])
    print(f"  解决方案: {sol_name}")
    print(f"  活动配置: {active_cfg} | {active_platform}")
    print(f"  可用配置总数: {len(configs)}")
    for cfg in configs:
        marker = " (Active)" if cfg.get("isActive") else ""
        print(f"    * {cfg.get('fullName')}{marker}")
    assert len(configs) > 0, "No solution configurations returned"
    assert any(c.get("isActive") for c in configs), "No active configuration marked"

    # 3. vs_navigate_to (方案相对路径导航定位到 Calculator.cs 第 7 行)
    target_rel_path = "SampleApp/Services/Calculator.cs"
    print(f"\n[3/6] 方案相对路径导航定位 (vs_navigate_to -> {target_rel_path}:7:5)")
    nav_res = call_tool("vs_navigate_to", {
        "filePath": target_rel_path,
        "line": 7,
        "column": 5
    })
    print(f"  导航响应: success={nav_res.get('success')}, path={nav_res.get('filePath')}, line={nav_res.get('line')}, col={nav_res.get('column')}")
    assert nav_res.get("success") is True, f"Navigation failed: {nav_res}"
    assert nav_res.get("line") == 7, f"Expected line 7, got {nav_res.get('line')}"

    # 4. vs_get_active_document (验证前台文档状态与光标停靠)
    print("\n[4/6] 探测前台活动文档与光标位置 (vs_get_active_document)")
    time.sleep(0.5)
    doc_res = call_tool("vs_get_active_document")
    assert not doc_res.get("isError"), f"vs_get_active_document returned error: {doc_res}"
    has_doc = doc_res.get("hasActiveDocument")
    print(f"  是否有活动文档: {has_doc}")
    assert has_doc is True, "Expected hasActiveDocument to be True"
    print(f"    * 文件名: {doc_res.get('fileName')}")
    print(f"    * 物理路径: {doc_res.get('filePath')}")
    print(f"    * 语言: {doc_res.get('language')}")
    print(f"    * 未保存脏状态: {doc_res.get('isDirty')}")
    print(f"    * 总行数: {doc_res.get('lineCount')}")
    print(f"    * 光标位置: Line {doc_res.get('cursorLine')}, Column {doc_res.get('cursorColumn')}")
    assert "Calculator.cs" in doc_res.get("fileName", ""), f"Unexpected active document: {doc_res.get('fileName')}"
    assert doc_res.get("cursorLine") == 7, f"Expected cursor at line 7, but was {doc_res.get('cursorLine')}"

    # 5. 绝对路径导航与文档切换测试 (切换到 Program.cs 第 10 行)
    abs_path = os.path.abspath("sample/SampleApp/Program.cs")
    print(f"\n[5/6] 绝对路径导航与跨文档切换 (vs_navigate_to -> {abs_path}:10:1)")
    nav_abs = call_tool("vs_navigate_to", {
        "filePath": abs_path,
        "line": 10,
        "column": 1
    })
    print(f"  导航响应: success={nav_abs.get('success')}, line={nav_abs.get('line')}")
    assert nav_abs.get("success") is True, f"Absolute path navigation failed: {nav_abs}"
    time.sleep(0.5)
    doc_res2 = call_tool("vs_get_active_document")
    print(f"  切换后活动文档: {doc_res2.get('fileName')}, 光标位置: Line {doc_res2.get('cursorLine')}")
    assert "Program.cs" in doc_res2.get("fileName", ""), f"Expected Program.cs active, got: {doc_res2.get('fileName')}"
    assert doc_res2.get("cursorLine") == 10, f"Expected cursor at line 10, got {doc_res2.get('cursorLine')}"

    # 6. 逆向防卫测试：导航至不存在的文件
    print("\n[6/6] 逆向防卫测试：导航至不存在的文件")
    non_existent = "sample/SampleApp/Services/NonExistent_NoSuchFile_12345.cs"
    err_res = call_tool("vs_navigate_to", {"filePath": non_existent})
    print(f"  调用返回: {err_res}")
    is_err = err_res.get("isError") or "error" in str(err_res).lower() or "file_not_found" in str(err_res).lower()
    assert is_err, f"Expected file_not_found error for non-existent file, got: {err_res}"
    print("    * 成功拦截不存在的文件，返回结构化错误 (file_not_found)")

    print("\n==================================================================")
    print("  Phase 4A 全部 6 项在线实测验证 100% 通过! (PASS)                 ")
    print("==================================================================")

if __name__ == "__main__":
    main()
