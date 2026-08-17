using VsDebugMcp.Host;
using VsDebugMcp.Protocol;

var pipeName = GetOption(args, "--pipe") ?? PipeNames.ForCurrentUser();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellation.Cancel();
};

try
{
	await using var client = new BridgeClient(pipeName);
	await client.ConnectAsync(TimeSpan.FromSeconds(5), cancellation.Token);

	var handshake = await client.HandshakeAsync(cancellation.Token);
	Console.WriteLine($"Connected to VS {handshake.VisualStudioVersion} (PID {handshake.VisualStudioProcessId}).");

	var health = await client.GetHealthAsync(cancellation.Token);
	Console.WriteLine($"Bridge health: {health.Status} at {health.UtcTimestamp}.");

	var capabilities = await client.GetCapabilitiesAsync(cancellation.Token);
	foreach (var capability in capabilities.Capabilities)
	{
		Console.WriteLine($"Capability: {capability.Name} v{capability.Version} (stub={capability.IsStub}).");
	}
}
catch (OperationCanceledException)
{
	Console.Error.WriteLine("Operation cancelled.");
	return 2;
}
catch (BridgeRpcException exception)
{
	Console.Error.WriteLine($"{exception.Code}: {exception.Message}");
	return 1;
}
catch (Exception exception)
{
	Console.Error.WriteLine($"{BridgeErrorCodes.BridgeUnavailable}: {exception.Message}");
	return 1;
}

return 0;

static string? GetOption(string[] arguments, string name)
{
	for (var index = 0; index < arguments.Length - 1; index++)
	{
		if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
		{
			return arguments[index + 1];
		}
	}

	return null;
}
