using System.Net.Sockets;
using System.Text;

// The tests swap the static Log sinks, so they must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Herdr.Tests;

// Collects what the code under test logs, and restores the console sinks after.
sealed class LogCapture : IDisposable
{
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public LogCapture()
    {
        Log.Info = Infos.Add;
        Log.Warning = Warnings.Add;
        Log.Error = Errors.Add;
    }

    public void Dispose()
    {
        Log.Info = Console.WriteLine;
        Log.Warning = Console.Error.WriteLine;
        Log.Error = Console.Error.WriteLine;
    }
}

// A stand-in herdr server on a scratch socket. `Behaviour` gets each request
// line and the connection to answer on.
sealed class FakeHerdr : IDisposable
{
    private readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource stop = new();

    public FakeHerdr()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("herdr-logi-").FullName;
        SocketPath = Path.Combine(Directory, "h.sock");
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        listener.Listen();
        _ = AcceptAsync();
    }

    public string Directory { get; }
    public string SocketPath { get; }
    public Func<Socket, string, Task> Behaviour { get; set; } = (_, _) => Task.CompletedTask;

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            Socket connection;
            try
            {
                connection = await listener.AcceptAsync(stop.Token);
            }
            catch (Exception)
            {
                return;
            }
            _ = ServeAsync(connection);
        }
    }

    private async Task ServeAsync(Socket connection)
    {
        using (connection)
        {
            var buffer = new List<byte>();
            var chunk = new byte[4096];
            while (true)
            {
                var read = await connection.ReceiveAsync(chunk);
                if (read == 0) return;
                buffer.AddRange(chunk.AsSpan(0, read));
                var end = buffer.IndexOf((byte)'\n');
                if (end == -1) continue;
                await Behaviour(connection, Encoding.UTF8.GetString(buffer.GetRange(0, end).ToArray()));
                return;
            }
        }
    }

    public static async Task SendAndClose(Socket connection, string text)
    {
        await connection.SendAsync(Encoding.UTF8.GetBytes(text));
        connection.Shutdown(SocketShutdown.Both);
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Dispose();
        System.IO.Directory.Delete(Directory, recursive: true);
    }
}
