#include "core/logger.h"
#include <iostream>

namespace SampleCMake::Core {

void Logger::Log(LogLevel level, std::string_view message) {
    const char* prefix = "[INFO]";
    switch (level) {
        case LogLevel::Info:    prefix = "[INFO]"; break;
        case LogLevel::Warning: prefix = "[WARN]"; break;
        case LogLevel::Error:   prefix = "[ERR ]"; break;
    }
    std::cout << prefix << " " << message << std::endl;
}

void Logger::Info(std::string_view message) {
    Log(LogLevel::Info, message);
}

void Logger::Warn(std::string_view message) {
    Log(LogLevel::Warning, message);
}

void Logger::Error(std::string_view message) {
    Log(LogLevel::Error, message);
}

} // namespace SampleCMake::Core
