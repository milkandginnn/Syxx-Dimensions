// DNSResolver.Transport.cs — low-level network I/O (UDP, TCP, stream helpers).
// B005: extracted from DNSResolver.cs to separate transport concerns from parsing and orchestration.

using System.Net.Sockets;

namespace Syxx.Kit
{
    public static partial class DNSResolver
    {
        private static async Task<byte[]> SendUDPAsync(byte[] packet, string server, ushort port)
        {
            using (var client = new UdpClient())
            {
                client.Connect(server, port);
                client.Client.SendTimeout = 2000;
                client.Client.ReceiveTimeout = 2000;

                await client.SendAsync(packet, packet.Length);

                var receiveTask = client.ReceiveAsync();
                var delayTask = Task.Delay(2000);

                var completedTask = await Task.WhenAny(receiveTask, delayTask);
                if (completedTask == receiveTask)
                {
                    var result = await receiveTask;
                    return result.Buffer;
                }
                else
                {
                    // Observe the loser's exception to prevent unobserved task exceptions.
                    _ = receiveTask.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("UDP Connection Timeout");
                }
            }
        }

        private static async Task<byte[]> SendTCPAsync(byte[] packet, string server, ushort port)
        {
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(server, port);
                if (await Task.WhenAny(connectTask, Task.Delay(2000)) != connectTask)
                {
                    throw new TimeoutException("TCP DNS connection timeout");
                }
                await connectTask;

                using (var stream = client.GetStream())
                {
                    var lengthPrefix = new[] { (byte)((packet.Length >> 8) & 0xff), (byte)(packet.Length & 0xff) };
                    await stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
                    await stream.WriteAsync(packet, 0, packet.Length);

                    var responseLengthBytes = await ReadExactAsync(stream, 2, 2000);
                    int responseLength = (responseLengthBytes[0] << 8) | responseLengthBytes[1];
                    return await ReadExactAsync(stream, responseLength, 2000);
                }
            }
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, int timeoutMs)
        {
            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                var readTask = stream.ReadAsync(buffer, offset, length - offset);
                if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)) != readTask)
                {
                    throw new TimeoutException("TCP DNS read timeout");
                }
                int read = await readTask;
                if (read == 0)
                {
                    throw new IOException("TCP DNS stream closed before response completed.");
                }
                offset += read;
            }
            return buffer;
        }
    }
}
