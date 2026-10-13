#include "core/logger.h"
#include "calculator/math_ops.h"
#include <iostream>
#include <string>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <dbghelp.h>

static LONG WINAPI WriteCrashMinidump(EXCEPTION_POINTERS* excPointers, const char* dumpFilePath) {
    HANDLE hFile = CreateFileA(
        dumpFilePath,
        GENERIC_WRITE,
        FILE_SHARE_READ,
         nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (hFile == INVALID_HANDLE_VALUE) {
        return EXCEPTION_EXECUTE_HANDLER;
    }

    MINIDUMP_EXCEPTION_INFORMATION mei{};
    mei.ThreadId = GetCurrentThreadId();
    mei.ExceptionPointers = excPointers;
    mei.ClientPointers = FALSE;

    MINIDUMP_TYPE dumpType = static_cast<MINIDUMP_TYPE>(
        MiniDumpWithDataSegs |
        MiniDumpWithFullMemory |
        MiniDumpWithHandleData |
        MiniDumpWithThreadInfo |
        MiniDumpWithUnloadedModules);

    MiniDumpWriteDump(
        GetCurrentProcess(),
        GetCurrentProcessId(),
        hFile,
        dumpType,
        excPointers ? &mei : nullptr,
        nullptr,
        nullptr);

    CloseHandle(hFile);
    return EXCEPTION_EXECUTE_HANDLER;
}

__declspec(noinline) static int TriggerSampleAccessViolation(const wchar_t* configPath, const char* moduleTag, int indexOffset) {
    // Intentionally dereference an invalid address when indexOffset == -1 to simulate Issue #4 crash
    volatile const wchar_t* badBase = reinterpret_cast<const wchar_t*>(0x00000000DEAD0000ULL);
    if (indexOffset < 0 && configPath != nullptr && moduleTag != nullptr) {
        return static_cast<int>(badBase[indexOffset]);
    }
    return 0;
}

static int GenerateCrashDump(const char* dumpFilePath) {
    const wchar_t* activeConfigPath = L"D:\\SampleCMake\\config\\PrintConfig.ini";
    const char* activeModuleTag = "SampleCMake-Phase8B-DumpTarget";
    int negativeIndex = -1;

    __try {
        return TriggerSampleAccessViolation(activeConfigPath, activeModuleTag, negativeIndex);
    }
    __except (WriteCrashMinidump(GetExceptionInformation(), dumpFilePath)) {
        std::cout << "Generated crash minidump at: " << dumpFilePath << std::endl;
        return 0;
    }
}
#endif

int main(int argc, char* argv[]) {
#ifdef _WIN32
    if (argc >= 2 && std::string(argv[1]) == "--generate-dmp") {
        const char* outPath = (argc >= 3) ? argv[2] : "crash_sample.dmp";
        return GenerateCrashDump(outPath);
    }
#endif

    SampleCMake::Core::Logger::Info("SampleCMakeApp starting...");

    int a = 20;
    int b = 6;

    int sum = SampleCMake::Modules::MathOps::Add(a, b);
    int diff = SampleCMake::Modules::MathOps::Subtract(a, b);
    int prod = SampleCMake::Modules::MathOps::Multiply(a, b);
    double quot = SampleCMake::Modules::MathOps::Divide(a, b);
    int fact = SampleCMake::Modules::MathOps::Factorial(5);

    std::cout << "Results:" << std::endl;
    std::cout << "  " << a << " + " << b << " = " << sum << std::endl;
    std::cout << "  " << a << " - " << b << " = " << diff << std::endl;
    std::cout << "  " << a << " * " << b << " = " << prod << std::endl;
    std::cout << "  " << a << " / " << b << " = " << quot << std::endl;
    std::cout << "  5! = " << fact << std::endl;

    SampleCMake::Core::Logger::Info("SampleCMakeApp finished successfully.");
    return 0;
}
