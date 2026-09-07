import urllib.request
import json
import time
import sys

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
    print("==================================================")
    print("  Visual Studio MCP Test Explorer 全量端到端验收  ")
    print("==================================================")

    # 1. vs_health & vs_capabilities
    print("\n[1/5] 健康检查与能力发现")
    health = call_tool("vs_health")
    print(f"  vs_health: status={health.get('status')}, vsInstanceId={health.get('selectedInstance', {}).get('vsInstanceId')}")
    caps = call_tool("vs_capabilities")
    test_caps = [c for c in caps.get("capabilities", []) if "test" in c.get("name")]
    print("  测试能力清单 (isStub 应全为 false):")
    for c in test_caps:
        print(f"    - {c.get('name')}: isStub={c.get('isStub')}")

    # 2. vs_get_tests
    print("\n[2/5] 测试用例发现与过滤")
    tests = call_tool("vs_get_tests")
    total = tests.get("totalCount", 0)
    print(f"  全量发现用例数: {total}")
    multiply_id = None
    for t in tests.get("tests", []):
        print(f"    * [{t.get('testId')}] {t.get('displayName')} (state: {t.get('state')})")
        if "Multiply" in t.get("displayName", ""):
            multiply_id = t.get("testId")

    filtered = call_tool("vs_get_tests", {"filter": "Multiply"})
    print(f"  过滤 'Multiply' 结果数: {filtered.get('totalCount')}")

    # 3. vs_run_tests (全量执行)
    print("\n[3/5] 全量测试执行与状态轮询")
    run_all = call_tool("vs_run_tests")
    run_all_id = run_all.get("testRunId")
    print(f"  已发起全量运行: testRunId={run_all_id}, 状态={run_all.get('state')}")
    for i in range(20):
        time.sleep(1)
        st = call_tool("vs_get_test_run_status", {"testRunId": run_all_id})
        state = st.get("state")
        print(f"    轮询 [{i+1}s]: state={state}, passed={st.get('passedCount')}, failed={st.get('failedCount')}")
        if state in ("completed", "failed", "cancelled"):
            print("    全量运行完成！结果摘要:")
            print(f"      Passed: {st.get('passedCount')}, Failed: {st.get('failedCount')}, Duration: {st.get('durationMs'):.1f}ms")
            for r in st.get("results", []):
                print(f"        - {r.get('displayName')}: {r.get('outcome')} ({r.get('durationMs')}ms)")
            break

    # 4. 指定用例单测运行 (如 Multiply)
    if multiply_id:
        print(f"\n[4/5] 指定单用例测试执行 (Multiply: {multiply_id})")
        run_single = call_tool("vs_run_tests", {"testIds": [multiply_id]})
        run_single_id = run_single.get("testRunId")
        print(f"  已发起单用例运行: testRunId={run_single_id}")
        for i in range(20):
            time.sleep(1)
            st = call_tool("vs_get_test_run_status", {"testRunId": run_single_id})
            state = st.get("state")
            print(f"    轮询 [{i+1}s]: state={state}, passed={st.get('passedCount')}, total={st.get('totalCount')}")
            if state in ("completed", "failed", "cancelled"):
                print(f"    单用例测试完成！Passed: {st.get('passedCount')}, Total: {st.get('totalCount')}")
                break

    # 5. 并发互斥与取消校验
    print("\n[5/5] 并发互斥校验 (test_run_busy)")
    run_concur = call_tool("vs_run_tests")
    concur_id = run_concur.get("testRunId")
    print(f"  已启动任务 1: testRunId={concur_id}")
    # 立即触发并发第二次请求
    run_busy = call_tool("vs_run_tests")
    print("  立即发起任务 2 (预期返回互斥错误):")
    print("   ", json.dumps(run_busy, ensure_ascii=False))

    # 取消当前任务
    print("  测试任务取消 vs_cancel_test_run:")
    cancel_res = call_tool("vs_cancel_test_run", {"testRunId": concur_id})
    print("   ", json.dumps(cancel_res, ensure_ascii=False))

    print("\n==================================================")
    print("                验收执行完成！                     ")
    print("==================================================")

if __name__ == "__main__":
    main()
