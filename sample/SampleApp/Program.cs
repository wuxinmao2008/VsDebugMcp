using SampleApp.Models;
using SampleApp.Services;
using SampleLib.Diagnostics;

Logger.Info("Starting SampleApp...");

var item = new Item { Id = 1, Name = "Widget", Price = 9.99 };
Logger.Info($"Created item: {item.Name} (${item.Price})");

var sum = Calculator.Add(10, 20);
Logger.Info($"Sum: {sum}");

Console.WriteLine("SampleApp completed successfully.");
