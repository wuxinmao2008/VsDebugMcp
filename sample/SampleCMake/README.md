# SampleCMake

This is a standardized, nested modern CMake test project designed for Visual Studio 2022 / 2026 (VS 18.x) and `VsDebugMcp` integration testing.

## Directory Structure

```text
sample/SampleCMake/
├── CMakeLists.txt                # Root CMake project file
├── CMakePresets.json             # Visual Studio CMake Presets (x64-Debug, x64-Release)
├── .gitignore                    # Build output exclusions (build, out, .vs)
├── src/
│   ├── CMakeLists.txt            # Subdirectory aggregator
│   ├── core/                     # Core utility library (static lib)
│   │   ├── CMakeLists.txt
│   │   ├── include/
│   │   │   └── core/
│   │   │       └── logger.h
│   │   └── src/
│   │       └── logger.cpp
│   ├── modules/                  # Nested functional modules
│   │   └── calculator/           # Math calculation module (depends on sample_core)
│   │       ├── CMakeLists.txt
│   │       ├── include/
│   │       │   └── calculator/
│   │       │       └── math_ops.h
│   │       └── src/
│   │           └── math_ops.cpp
│   └── app/                      # Application executable (SampleCMakeApp)
│       ├── CMakeLists.txt
│       └── main.cpp
└── tests/                        # CTest unit tests
    ├── CMakeLists.txt
    └── test_math.cpp
```

## Features

1. **Multi-level Folder Nesting**:
   - Depth 1: `src/`, `tests/`
   - Depth 2: `src/core/`, `src/modules/calculator/`, `src/app/`
   - Depth 3: `include/core/`, `include/calculator/`, `src/`

2. **Native VS CMake Presets**:
   - `x64-Debug` (Ninja / Debug)
   - `x64-Release` (Ninja / Release)

3. **CTest Integration**:
   - Defines test `MathOperationsTest` executable with assertion testing.

## How to Test in Visual Studio

1. In Visual Studio, select **File -> Open -> Folder...** and choose this `SampleCMake` folder.
2. Visual Studio will recognize `CMakeLists.txt` and `CMakePresets.json`.
3. In `VsDebugMcp`, the MCP tools will be validated against this nested project structure for:
   - Folder workspace detection
   - Recursive file indexing & filtering
   - CMake Presets enumeration & selection
   - CMake configure & build execution
   - Breakpoint setting & debugger execution
