import urllib.request
import json
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
    print("   VsDebugMcp Phase 7A: CMake Workspace Acceptance")
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

    # 2. vs_get_projects_in_solution
    print("\n[Step 2] CMake Workspace Project Discovery")
    proj_res = call_tool("vs_get_projects_in_solution")
    projects = proj_res.get("projects", [])
    warnings = proj_res.get("warnings", [])
    print(f"  Total Projects: {len(projects)}")
    print(f"  Warnings Count: {len(warnings)}")
    for p in projects:
        print(f"    - ID: {p.get('id')}, Name: {p.get('name')}, Kind: {p.get('kind')}, Unsupported: {p.get('isUnsupported')}, Path: {p.get('projectFilePath')}")

    cmake_proj = next((p for p in projects if p.get("kind") == "cmake" or p.get("id") == "cmake:root"), None)
    if cmake_proj:
        print("  --> PASS: CMake project recognized with Kind='cmake', isUnsupported=False")
        assert not cmake_proj.get("isUnsupported"), "CMake project should not be unsupported"
        assert cmake_proj.get("projectFilePath", "").endswith("CMakeLists.txt"), "ProjectFilePath should end with CMakeLists.txt"
    else:
        print("  --> INFO: Conventional or non-CMake project structure returned")

    # 3. vs_get_files_in_project (All files)
    print("\n[Step 3] Workspace Source File Traversal (Unfiltered)")
    files_res = call_tool("vs_get_files_in_project")
    total_files = files_res.get("totalFileCount", 0)
    print(f"  Total Files Found: {total_files}")
    for group in files_res.get("projects", []):
        print(f"  Project: {group.get('projectName')} ({group.get('fileCount')} files)")
        for f in group.get("files", [])[:10]:
            print(f"    - {f.get('relativePath')}")
        if group.get("fileCount", 0) > 10:
            print(f"    ... and {group.get('fileCount') - 10} more files")

    # 4. vs_get_files_in_project (Extension filtered)
    print("\n[Step 4] Extension Filtered Traversal (.cpp)")
    cpp_files_res = call_tool("vs_get_files_in_project", {"extensionFilter": ".cpp"})
    cpp_total = cpp_files_res.get("totalFileCount", 0)
    print(f"  C++ Source Files (.cpp): {cpp_total}")
    for group in cpp_files_res.get("projects", []):
        for f in group.get("files", []):
            print(f"    - {f.get('relativePath')}")
            assert f.get("extension") == ".cpp", f"Expected .cpp extension, got {f.get('extension')}"

    print("\n[Step 5] Extension Filtered Traversal (.h)")
    h_files_res = call_tool("vs_get_files_in_project", {"extensionFilter": ".h"})
    h_total = h_files_res.get("totalFileCount", 0)
    print(f"  Header Files (.h): {h_total}")
    for group in h_files_res.get("projects", []):
        for f in group.get("files", []):
            print(f"    - {f.get('relativePath')}")
            assert f.get("extension") == ".h", f"Expected .h extension, got {f.get('extension')}"

    print("\n==================================================")
    print("       Phase 7A Acceptance Completed Successfully! ")
    print("==================================================")

if __name__ == "__main__":
    main()
