using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using VsDebugMcp.Host;
using VsDebugMcp.Protocol;

VsHostOptions options;
try
{
	options = HostCommandLine.Parse(args);
}
catch (ArgumentException exception)
{
	Console.Error.WriteLine(exception.Message);
	PrintUsage(Console.Error);
	return 2;
}

if (options.Mode == HostMode.Help)
{
	PrintUsage(Console.Out);
	return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellation.Cancel();
};

if (options.Mode == HostMode.Http && !OperatingSystem.IsWindows())
{
	Console.Error.WriteLine("Named Pipe HTTP transport is supported only on Windows.");
	return 2;
}

return options.Mode switch
{
	HostMode.Smoke => await RunSmokeAsync(options, cancellation.Token),
	HostMode.Http => await RunHttpAsync(options, cancellation.Token),
	_ => await RunMcpAsync(options, cancellation.Token)
};

static async Task<int> RunMcpAsync(VsHostOptions options, CancellationToken cancellationToken)
{
	var builder = Host.CreateApplicationBuilder();
	builder.Logging.ClearProviders();
	builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

	var tools = new McpTools(new BridgeService(options));
	builder.Services
		.AddMcpServer()
		.WithStdioServerTransport()
		.WithTools<McpTools>(tools);

	await builder.Build().RunAsync(cancellationToken);
	return 0;
}

[SupportedOSPlatform("windows")]
static async Task<int> RunHttpAsync(VsHostOptions options, CancellationToken cancellationToken)
{
	var builder = WebApplication.CreateBuilder();
	builder.Logging.ClearProviders();
	builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
	builder.WebHost.ConfigureKestrel(serverOptions =>
	{
		serverOptions.ListenNamedPipe(options.McpPipeName, listenOptions =>
		{
			listenOptions.Protocols = HttpProtocols.Http1;
		});
	});
	builder.WebHost.UseNamedPipes(namedPipeOptions => namedPipeOptions.CurrentUserOnly = true);

	var tools = new McpTools(new BridgeService(options));
	builder.Services
		.AddMcpServer()
		.WithHttpTransport(httpOptions => httpOptions.Stateless = true)
		.WithTools<McpTools>(tools);

	var app = builder.Build();
	app.MapMcp();
	await app.RunAsync(cancellationToken);
	return 0;
}

static async Task<int> RunSmokeAsync(VsHostOptions options, CancellationToken cancellationToken)
{
	try
	{
		var service = new BridgeService(options);
		var health = await service.GetHealthAsync(cancellationToken);
		Console.WriteLine($"Bridge health: {health.Status} at {health.UtcTimestamp}.");

		var capabilities = await service.GetCapabilitiesAsync(cancellationToken);
		Console.WriteLine(
			$"Connected to VS {capabilities.VisualStudioVersion} (PID {capabilities.VisualStudioProcessId}).");
		foreach (var capability in capabilities.Capabilities)
		{
			Console.WriteLine($"Capability: {capability.Name} v{capability.Version} (stub={capability.IsStub}).");
		}

		return 0;
	}
	catch (OperationCanceledException)
	{
		Console.Error.WriteLine("Operation cancelled.");
		return 2;
	}
	catch (BridgeServiceException exception)
	{
		Console.Error.WriteLine($"{exception.Code}: {exception.Message}");
		return 1;
	}
	catch (Exception)
	{
		Console.Error.WriteLine($"{BridgeErrorCodes.InternalError}: The host request failed.");
		return 1;
	}
}

static void PrintUsage(TextWriter writer) => writer.WriteLine(
	"Usage: VsDebugMcp.Host [--mcp|--http|--smoke] [--pipe <name>] [--mcp-pipe <name>] [--connect-timeout-seconds <1-30>]");
