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
    try:
        r = urllib.request.urlopen(req)
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
    print("   VsDebugMcp Phase 7B: CMakePresets & Build Acceptance")
    print("==================================================")

    # 1. vs_health
    print("\n[Step 1] Health and Instance Check")
    health = call_tool("vs_health")
    status = health.get("status")
    selected_inst = health.get("selectedInstance", {})
    vs_id = selected_inst.get("vsInstanceId")
    sol_name = selected_inst.get("solutionName")
    sol_path = selected_inst.get("solutionFilePath")
    print(f"  Health status: {status}")
    print(f"  Instance ID:   {vs_id}")
    print(f"  Solution Name: {sol_name}")
    print(f"  Solution Path: {sol_path}")
    assert status == "ok", f"Health status not ok: {status}"

    # 2. vs_get_solution_configurations
    print("\n[Step 2] CMakePresets Discovery (vs_get_solution_configurations)")
    cfg_res = call_tool("vs_get_solution_configurations")
    cfgs = cfg_res.get("configurations", [])
    active_cfg = cfg_res.get("activeConfigurationName")
    active_plat = cfg_res.get("activePlatformName")
    print(f"  Active Configuration: {active_cfg} ({active_plat})")
    print(f"  Configurations found: {len(cfgs)}")
    for c in cfgs:
        print(f"    - Name: {c.get('name')}, Platform: {c.get('platformName')}, Full: {c.get('fullName')}, Active: {c.get('isActive')}")

    names = [c.get("name") for c in cfgs]
    assert "x64-Debug" in names, "x64-Debug should be discovered in presets"
    assert "x64-Release" in names, "x64-Release should be discovered in presets"
    assert "windows-base" not in names, "hidden preset windows-base must not be present"
    print("  --> PASS: CMakePresets parsed correctly and hidden presets filtered!")

    # 3. vs_set_solution_configuration (Switch to Release, then back to Debug)
    print("\n[Step 3] Configuration Switching (vs_set_solution_configuration)")
    set_res1 = call_tool("vs_set_solution_configuration", {"configuration": "x64-Release"})
    print(f"  Switch to x64-Release: Success={set_res1.get('success')}, Prev={set_res1.get('previousConfiguration')}, Active={set_res1.get('activeConfiguration')}")
    assert set_res1.get("success") == True, "Failed to switch configuration to x64-Release"
    assert set_res1.get("activeConfiguration") == "x64-Release", "Active configuration should be x64-Release"

    # Verify via get
    cfg_res2 = call_tool("vs_get_solution_configurations")
    assert cfg_res2.get("activeConfigurationName") == "x64-Release", "Active configuration should reflect x64-Release"

    # Switch back to x64-Debug
    set_res2 = call_tool("vs_set_solution_configuration", {"configuration": "x64-Debug"})
    print(f"  Switch back to x64-Debug: Success={set_res2.get('success')}, Prev={set_res2.get('previousConfiguration')}, Active={set_res2.get('activeConfiguration')}")
    assert set_res2.get("success") == True, "Failed to switch back to x64-Debug"
    assert set_res2.get("activeConfiguration") == "x64-Debug", "Active configuration should be x64-Debug"
    print("  --> PASS: Configuration switching validated!")

    # 4. vs_run_build
    print("\n[Step 4] Trigger CMake Build (vs_run_build)")
    build_res = call_tool("vs_run_build", {"configuration": "x64-Debug"})
    task_id = build_res.get("buildTaskId")
    initial_state = build_res.get("state")
    print(f"  Build Task ID:  {task_id}")
    print(f"  Initial State:  {initial_state}")
    print(f"  Configuration:  {build_res.get('configuration')} ({build_res.get('platform')})")
    print(f"  Requested At:   {build_res.get('requestedAtUtc')}")
    assert task_id, "Build task ID should not be empty"

    # 5. vs_get_build_status polling
    print("\n[Step 5] Poll Build Status (vs_get_build_status)")
    terminal_states = {"succeeded", "failed", "cancelled"}
    final_status = None
    for attempt in range(60):
        status_res = call_tool("vs_get_build_status", {"buildTaskId": task_id})
        cur_state = status_res.get("state")
        print(f"  [Poll #{attempt+1}] State: {cur_state}, StartedAt: {status_res.get('startedAtUtc')}, CompletedAt: {status_res.get('completedAtUtc')}")
        if cur_state in terminal_states:
            final_status = status_res
            break
        time.sleep(1)

    assert final_status is not None, "Build did not reach a terminal state within timeout"
    print(f"  Final Build State: {final_status.get('state')}, Succeeded: {final_status.get('succeeded')}")
    assert final_status.get("state") == "succeeded", f"Build did not succeed, final state: {final_status.get('state')}"
    assert final_status.get("succeeded") == True, "Build succeeded flag should be True"
    print("  --> PASS: CMake Build succeeded!")

    # 6. vs_get_output_window_logs
    print("\n[Step 6] Inspect Build Output Window (vs_get_output_window_logs)")
    logs_res = call_tool("vs_get_output_window_logs", {"source": "build", "maxChars": 5000})
    log_text = logs_res.get("text", "")
    print(f"  Output Window returned {len(log_text)} chars")
    snippet = "\n".join(log_text.splitlines()[-15:])
    print("  Recent Log Snippet:\n" + snippet)
    assert len(log_text) > 0, "Build output pane should not be empty"
    assert "CMake" in log_text or "生成" in log_text or "ninja" in log_text, "Output should contain CMake / build markers"
    print("  --> PASS: Build output successfully streamed to Visual Studio Output Window!")

    print("\n==================================================")
    print("   ALL PHASE 7B ACCEPTANCE CHECKS PASSED (100%)")
    print("==================================================")

if __name__ == "__main__":
    main()
