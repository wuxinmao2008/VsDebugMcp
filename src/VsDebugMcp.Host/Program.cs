using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VsDebugMcp.Host;

if (!OperatingSystem.IsWindows())
{
	Console.Error.WriteLine("VsDebugMcp.Host supports Windows only.");
	return 2;
}

if (args.Length != 0)
{
	Console.Error.WriteLine("VsDebugMcp.Host does not accept command-line transport options.");
	return 2;
}

using var singleInstance = HostSingleInstance.Acquire();
if (!singleInstance.Acquired)
{
	return 0;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	shutdown.Cancel();
};

var options = new VsHostOptions();
var registry = new VisualStudioInstanceRegistry(options, shutdown.Cancel);
var controlServer = new HostControlServer(options, registry, shutdown.Cancel);
var tools = new McpTools(new BridgeService(options, registry));

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Configuration["AllowedHosts"] = "127.0.0.1";
builder.WebHost.ConfigureKestrel(serverOptions =>
{
	serverOptions.Listen(IPAddress.Loopback, VsHostOptions.HttpPort, listenOptions =>
	{
		listenOptions.Protocols = HttpProtocols.Http1;
	});
});
builder.Services
	.AddMcpServer()
	.WithHttpTransport(httpOptions => httpOptions.Stateless = true)
	.WithTools<McpTools>(tools);

var app = builder.Build();
app.MapMcp();

Task? controlTask = null;
Task? monitorTask = null;

try
{
	await app.StartAsync(shutdown.Token);
	controlTask = controlServer.RunAsync(shutdown.Token);
	monitorTask = registry.MonitorAsync(shutdown.Token);
	await app.WaitForShutdownAsync(shutdown.Token);
	return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
	return 0;
}
catch (IOException)
{
	Console.Error.WriteLine("host_port_unavailable: 127.0.0.1:43260 is unavailable.");
	return 1;
}
finally
{
	shutdown.Cancel();
	try
	{
		await Task.WhenAll(
			controlTask ?? Task.CompletedTask,
			monitorTask ?? Task.CompletedTask);
	}
	catch (OperationCanceledException)
	{
	}
}
