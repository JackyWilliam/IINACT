#nullable enable

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NetCoreServer;

namespace RainbowMage.OverlayPlugin.WebSocket;

internal class OverlayServer : WsServer
{
    private TinyIoCContainer Container { get; }
    private ILogger Logger { get; }
    private readonly Action<OverlayServer, SocketError> fatalAcceptError;
    private int fatalAcceptErrorHandled;
    
    public OverlayServer(
        IPAddress address,
        int port,
        TinyIoCContainer container,
        Action<OverlayServer, SocketError> fatalAcceptError) : base(address, port)
    {
        ArgumentNullException.ThrowIfNull(fatalAcceptError);
        Container = container;
        Logger = container.Resolve<ILogger>();
        this.fatalAcceptError = fatalAcceptError;
    }

    protected override TcpSession CreateSession()
    {
        return new OverlaySession(this, Container);
    }

    protected override void OnError(SocketError error)
    {
        Logger.Log(LogLevel.Error, $"Overlay WebSocket server caught an error with code {error}");

        if (!IsFatalAcceptError(error) || Interlocked.Exchange(ref fatalAcceptErrorHandled, 1) != 0)
        {
            return;
        }

        try
        {
            fatalAcceptError(this, error);
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Error, $"Overlay WebSocket fatal error recovery failed: {ex}");
        }
        finally
        {
            if (IsAccepting)
            {
                try
                {
                    // NetCoreServer retries synchronously after OnError, so the bad listener must stop before returning.
                    Stop();
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Error, $"Overlay WebSocket failed to stop its invalid listener: {ex}");
                }
            }
        }
    }

    private static bool IsFatalAcceptError(SocketError error)
        => error is SocketError.NotSocket or SocketError.InvalidArgument;
}
    
