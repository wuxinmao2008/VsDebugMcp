using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VsDebugMcp.Protocol;

public static class PipeMessageFraming
{
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(BridgeJson.Serialize(message));
        if (payload.Length > BridgeProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException($"Message exceeds {BridgeProtocol.MaxMessageBytes} bytes.");
        }

        var header = new[]
        {
            (byte)payload.Length,
            (byte)(payload.Length >> 8),
            (byte)(payload.Length >> 16),
            (byte)(payload.Length >> 24)
        };

        await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);

        var length = header[0]
            | header[1] << 8
            | header[2] << 16
            | header[3] << 24;

        if (length <= 0 || length > BridgeProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException($"Invalid message length: {length}.");
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return BridgeJson.Deserialize<T>(System.Text.Encoding.UTF8.GetString(payload));
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The pipe closed before the message was complete.");
            }

            offset += read;
        }
    }
}