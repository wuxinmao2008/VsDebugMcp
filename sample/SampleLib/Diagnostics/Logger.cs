namespace SampleLib.Diagnostics;

public static class Logger
{
    public static void Info(string message) => Console.WriteLine($"[INFO] {message}");
    public static void Warn(string message) => Console.WriteLine($"[WARN] {message}");
}
