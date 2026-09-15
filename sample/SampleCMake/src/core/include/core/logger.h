#pragma once

#include <string>
#include <string_view>

namespace SampleCMake::Core {

enum class LogLevel {
    Info,
    Warning,
    Error
};

class Logger {
public:
    static void Log(LogLevel level, std::string_view message);
    static void Info(std::string_view message);
    static void Warn(std::string_view message);
    static void Error(std::string_view message);
};

} // namespace SampleCMake::Core
