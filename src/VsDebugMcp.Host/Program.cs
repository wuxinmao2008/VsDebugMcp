using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

return options.Mode == HostMode.Smoke
	? await RunSmokeAsync(options, cancellation.Token)
	: await RunMcpAsync(options, cancellation.Token);

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
	"Usage: VsDebugMcp.Host [--mcp|--smoke] [--pipe <name>] [--connect-timeout-seconds <1-30>]");
