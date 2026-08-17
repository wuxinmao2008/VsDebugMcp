using System.Runtime.Serialization;

namespace VsDebugMcp.Protocol;

public static class BridgeProtocol
{
	public const string Version = "1.0";
	public const int MaxMessageBytes = 1024 * 1024;
}

public static class BridgeMethods
{
	public const string Handshake = "handshake";
	public const string Health = "health";
	public const string Capabilities = "capabilities";
	public const string Shutdown = "shutdown";
}

public static class BridgeErrorCodes
{
	public const string ProtocolMismatch = "protocol_mismatch";
	public const string BridgeUnavailable = "bridge_unavailable";
	public const string InvalidRequest = "invalid_request";
	public const string Timeout = "timeout";
	public const string Cancelled = "cancelled";
	public const string InternalError = "internal_error";
}

[DataContract]
public sealed class BridgeRequest
{
	[DataMember(Name = "protocolVersion", Order = 1)]
	public string ProtocolVersion { get; set; } = BridgeProtocol.Version;

	[DataMember(Name = "requestId", Order = 2)]
	public string RequestId { get; set; } = string.Empty;

	[DataMember(Name = "method", Order = 3)]
	public string Method { get; set; } = string.Empty;

	[DataMember(Name = "payloadJson", Order = 4, EmitDefaultValue = false)]
	public string? PayloadJson { get; set; }
}

[DataContract]
public sealed class BridgeResponse
{
	[DataMember(Name = "protocolVersion", Order = 1)]
	public string ProtocolVersion { get; set; } = BridgeProtocol.Version;

	[DataMember(Name = "requestId", Order = 2)]
	public string RequestId { get; set; } = string.Empty;

	[DataMember(Name = "payloadJson", Order = 3, EmitDefaultValue = false)]
	public string? PayloadJson { get; set; }

	[DataMember(Name = "error", Order = 4, EmitDefaultValue = false)]
	public BridgeError? Error { get; set; }

	public static BridgeResponse Success<T>(string requestId, T payload) => new()
	{
		RequestId = requestId,
		PayloadJson = BridgeJson.Serialize(payload)
	};

	public static BridgeResponse Failure(string requestId, BridgeError error) => new()
	{
		RequestId = requestId,
		Error = error
	};
}

[DataContract]
public sealed class BridgeError
{
	[DataMember(Name = "code", Order = 1)]
	public string Code { get; set; } = BridgeErrorCodes.InternalError;

	[DataMember(Name = "message", Order = 2)]
	public string Message { get; set; } = string.Empty;

	[DataMember(Name = "details", Order = 3, EmitDefaultValue = false)]
	public string? Details { get; set; }

	[DataMember(Name = "retryable", Order = 4)]
	public bool Retryable { get; set; }
}
