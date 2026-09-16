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
    print("   VsDebugMcp Phase 7C: CTest & CMake Debugging Acceptance")
    print("==================================================")

    # 1. Health check
    print("\n[Step 1] Health and Instance Verification")
    health = call_tool("vs_health")
    status = health.get("status")
    selected_inst = health.get("selectedInstance", {})
    vs_id = selected_inst.get("vsInstanceId")
    sol_name = selected_inst.get("solutionName")
    print(f"  Health status: {status}")
    print(f"  Instance ID:   {vs_id}")
    print(f"  Solution Name: {sol_name}")
    assert status == "ok", f"Health status not ok: {status}"

    # 2. CTest Discovery (vs_get_tests)
    print("\n[Step 2] CTest Test Discovery (vs_get_tests)")
    tests_res = call_tool("vs_get_tests")
    tests = tests_res.get("tests", [])
    total_count = tests_res.get("totalCount", 0)
    print(f"  Discovered tests: {total_count}")
    for t in tests:
        print(f"    - ID: {t.get('testId')}, DisplayName: {t.get('displayName')}, Line: {t.get('lineNumber')}, Source: {t.get('source')}")

    ctest_items = [t for t in tests if (t.get("testId") or "").startswith("ctest:")]
    assert len(ctest_items) >= 1, f"Expected at least 1 CTest test, found: {len(ctest_items)}"
    math_test = next((t for t in ctest_items if "MathOperationsTest" in t.get("displayName", "")), None)
    assert math_test is not None, "MathOperationsTest was not discovered"
    print("  --> PASS: CTest discovery successfully found MathOperationsTest!")

    # Test filtering
    filtered_res = call_tool("vs_get_tests", {"filter": "Math"})
    filtered_tests = filtered_res.get("tests", [])
    assert any("MathOperationsTest" in t.get("displayName", "") for t in filtered_tests), "Filter by 'Math' failed"
    print("  --> PASS: Filtered discovery verified.")

    # 3. CTest Test Execution (vs_run_tests & vs_get_test_run_status)
    print("\n[Step 3] CTest Test Execution (vs_run_tests)")
    run_res = call_tool("vs_run_tests", {"testIds": ["ctest:MathOperationsTest"]})
    run_id = run_res.get("testRunId")
    run_state = run_res.get("state")
    print(f"  Run started: ID={run_id}, Initial State={run_state}")
    assert run_id, "TestRunId must not be empty"

    # Poll status
    terminal_states = {"completed", "failed", "cancelled", "Completed", "Failed", "Cancelled"}
    status_res = {}
    for attempt in range(30):
        time.sleep(1)
        status_res = call_tool("vs_get_test_run_status", {"testRunId": run_id})
        current_state = status_res.get("state")
        print(f"  [Poll {attempt+1}] State: {current_state}, Passed: {status_res.get('passedCount')}, Failed: {status_res.get('failedCount')}")
        if current_state in terminal_states:
            break

    final_state = status_res.get("state")
    assert str(final_state).lower() == "completed", f"Expected completed state, got: {final_state}"
    assert status_res.get("passedCount") >= 1, "Expected at least 1 passed test"
    assert status_res.get("failedCount") == 0, "Expected 0 failed tests"
    results = status_res.get("results", [])
    assert len(results) > 0, "Expected test results in status"
    first_res = results[0]
    print(f"  Test Result: ID={first_res.get('testId')}, Outcome={first_res.get('outcome')}, Duration={first_res.get('durationMs')}ms")
    assert str(first_res.get("outcome")).lower() == "passed", f"Outcome not passed: {first_res.get('outcome')}"
    print("  --> PASS: CTest execution and JUnit parsing verified successfully!")

    # 4. CMake Target Debug Launch (vs_debugger_start) with Breakpoint
    print("\n[Step 4] CMake Target Debug Launch with Breakpoint (vs_debugger_start)")
    # Clear existing breakpoints
    call_tool("vs_debugger_clear_breakpoints", {"clearAll": True})

    # Set breakpoint at main.cpp line 11
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    main_cpp = os.path.join(repo_root, "sample", "SampleCMake", "src", "app", "main.cpp")
    print(f"  Setting breakpoint in: {main_cpp}:11")
    bp_res = call_tool("vs_debugger_set_breakpoints", {
        "filePath": main_cpp,
        "line": 11
    })
    print(f"  Breakpoints set: {len(bp_res.get('breakpoints', []))}")
    assert len(bp_res.get('breakpoints', [])) >= 1, "Expected at least 1 breakpoint set"

    # Launch target: SampleCMakeApp.exe with waitForBreak=True
    print("  Launching target 'SampleCMakeApp.exe' with native debugger attached...")
    start_res = call_tool("vs_debugger_start", {
        "target": "SampleCMakeApp.exe",
        "waitForBreak": True,
        "timeoutMs": 15000
    })
    mode = start_res.get("currentMode") or start_res.get("mode")
    print(f"  Debugger start result: IsDebugging={start_res.get('isDebugging')}, Mode={mode}, LastBreakReason={start_res.get('lastBreakReason')}")
    assert start_res.get("isDebugging") == True, "Debugger should be in debugging mode"
    assert mode == "break", f"Expected break mode, got {mode}"

    # Verify call stack
    cs_res = call_tool("vs_debugger_get_call_stack")
    frames = cs_res.get("frames", [])
    print(f"  Call Stack: {len(frames)} frames")
    if frames:
        print(f"    Top Frame: {frames[0].get('functionName')} at {frames[0].get('fileName')}:{frames[0].get('lineNumber')}")
        assert "main" in frames[0].get("functionName", "").lower(), "Top frame should be main function"

    # Verify locals
    locals_res = call_tool("vs_debugger_get_locals")
    variables = locals_res.get("variables", [])
    print(f"  Locals ({len(variables)} variables):")
    for v in variables:
        print(f"    - {v.get('name')} = {v.get('value')} ({v.get('type')})")
    var_names = [v.get("name") for v in variables]
    assert "a" in var_names or "b" in var_names, "Expected local variable 'a' or 'b'"

    # Stop debugger
    print("  Stopping debugger...")
    stop_res = call_tool("vs_debugger_stop")
    print(f"  Stopped: IsDebugging={stop_res.get('isDebugging')}")
    call_tool("vs_debugger_clear_breakpoints", {"clearAll": True})
    print("  --> PASS: CMake target native debug launch & breakpoint inspection verified!")

    # 5. CTest Test Debugging (vs_debug_test_by_id)
    print("\n[Step 5] CTest Test Debug Launch (vs_debug_test_by_id)")
    test_math_cpp = os.path.join(repo_root, "sample", "SampleCMake", "tests", "test_math.cpp")
    print(f"  Setting breakpoint in test: {test_math_cpp}:9")
    call_tool("vs_debugger_set_breakpoints", {
        "filePath": test_math_cpp,
        "line": 9
    })

    print("  Starting debug for testId 'ctest:MathOperationsTest'...")
    debug_test_res = call_tool("vs_debug_test_by_id", {
        "testId": "ctest:MathOperationsTest",
        "waitForBreak": True,
        "timeoutMs": 15000
    })
    print(f"  DebugTest response: IsDebugging={debug_test_res.get('isDebugging')}, Mode={debug_test_res.get('debuggerMode')}")
    assert debug_test_res.get("isDebugging") == True, "Debugger should be active"
    assert debug_test_res.get("debuggerMode") == "break", f"Expected break mode, got: {debug_test_res.get('debuggerMode')}"

    # Check top frame
    top_frame = debug_test_res.get("topFrame", {})
    if top_frame:
        print(f"  Top frame: {top_frame.get('functionName')} at {top_frame.get('fileName')}:{top_frame.get('lineNumber')}")

    # Stop debugger
    call_tool("vs_debugger_stop")
    call_tool("vs_debugger_clear_breakpoints", {"clearAll": True})
    print("  --> PASS: CTest test debugging by ID verified successfully!")

    print("\n==================================================")
    print("   ALL PHASE 7C ACCEPTANCE TESTS PASSED!")
    print("==================================================")

if __name__ == "__main__":
    main()
