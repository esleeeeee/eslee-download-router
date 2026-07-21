using System.Buffers.Binary;
using System.Text.Json;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Ipc;

public static class LengthPrefixedJsonProtocol
{
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthBuffer = new byte[sizeof(int)];
        var firstRead = await ReadExactlyOrEndAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
        if (!firstRead)
        {
            return default;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length <= 0 || length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new InvalidDataException($"Message length {length} is outside the allowed range.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload, ProtocolJson.Options)
            ?? throw new InvalidDataException("Message JSON was empty.");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, ProtocolJson.Options);
        if (payload.Length <= 0 || payload.Length > ProtocolConstants.MaximumMessageBytes)
        {
            throw new InvalidDataException($"Serialized message length {payload.Length} is outside the allowed range.");
        }

        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (totalRead == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Message ended before its length prefix was complete.");
            }

            totalRead += read;
        }

        return true;
    }
}
