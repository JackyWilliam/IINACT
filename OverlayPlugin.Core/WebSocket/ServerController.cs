#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Advanced_Combat_Tracker;

namespace RainbowMage.OverlayPlugin.WebSocket;

public class ServerController
{
    private const int FatalErrorRecoveryDelayMilliseconds = 250;
    private readonly object lifecycleLock = new();
    private CancellationTokenSource? recoveryCancellation;
    private bool desiredRunning;

    public EventHandler<StateChangedArgs>? OnStateChanged;

    public ServerController(TinyIoCContainer container)
    {
        Container = container;
        Logger = container.Resolve<ILogger>();
        Config = container.Resolve<IPluginConfig>();
    }

    private TinyIoCContainer Container { get; }
    private ILogger Logger { get; }
    private OverlayServer? Server { get; set; }
    private IPluginConfig Config { get; }
    public bool Failed { get; private set; }
    public Exception? LastException { get; private set; }
    public bool Running
    {
        get
        {
            lock (lifecycleLock)
                return Server?.IsAccepting ?? false;
        }
    }
    public string? Address
    {
        get
        {
            lock (lifecycleLock)
                return Server?.Address;
        }
    }
    public int? Port
    {
        get
        {
            lock (lifecycleLock)
                return Server?.Port;
        }
    }
    public bool Secure => false;
    public Uri Uri => new($"{(Secure ? "wss" : "ws")}://{Address}:{Port}");

    public void Stop()
    {
        lock (lifecycleLock)
        {
            desiredRunning = false;
            CancelRecoveryLocked();
            StopServerLocked(Server);
            Server = null;

            Failed = false;
        }

        OnStateChanged?.Invoke(null, new StateChangedArgs(false, false));
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public bool IsSSLPossible()
    {
        return File.Exists(GetCertPath());
    }

    public void Start()
    {
        bool started;
        lock (lifecycleLock)
        {
            desiredRunning = true;
            CancelRecoveryLocked();
            started = StartServerLocked();
        }

        OnStateChanged?.Invoke(this, new StateChangedArgs(started, !started));
    }

    private bool StartServerLocked()
    {
        if (Server?.IsAccepting == true)
        {
            return true;
        }

        StopServerLocked(Server);
        Server = null;
        Failed = false;

        try
        {
            // TODO: add SSL support
            // var sslPath = GetCertPath();
            // var secure = _cfg.WSServerSSL && File.Exists(sslPath);

            var address = Config.WSServerIP == "*" ? IPAddress.Any : IPAddress.Parse(Config.WSServerIP);

            var server = new OverlayServer(address, Config.WSServerPort, Container, HandleFatalAcceptError);
            server.OptionReuseAddress = true;
            Server = server;

            if (!server.Start() || !server.IsAccepting)
            {
                throw new InvalidOperationException("Overlay WebSocket server did not enter the accepting state.");
            }

            LastException = null;
            return true;
        }
        catch (Exception e)
        {
            StopServerLocked(Server);
            Server = null;
            Failed = true;
            LastException = e;
            Logger.Log(LogLevel.Error, Resources.WSStartFailed, e);
            return false;
        }
    }

    private void HandleFatalAcceptError(OverlayServer source, SocketError error)
    {
        CancellationTokenSource? recovery = null;

        lock (lifecycleLock)
        {
            if (!ReferenceEquals(Server, source))
            {
                return;
            }

            // ProcessAccept retries as soon as this callback returns, so stopping cannot be deferred to the recovery task.
            StopServerLocked(source);
            Server = null;
            Failed = true;
            LastException = new SocketException((int)error);

            if (desiredRunning)
            {
                CancelRecoveryLocked();
                recovery = new CancellationTokenSource();
                recoveryCancellation = recovery;
            }
        }

        Logger.Log(
            LogLevel.Warning,
            $"Overlay WebSocket server stopped after fatal accept error {error}; scheduling one recovery attempt.");

        if (recovery is not null)
        {
            _ = RecoverAfterFatalErrorAsync(recovery, recovery.Token);
        }

        OnStateChanged?.Invoke(this, new StateChangedArgs(false, true));
    }

    private async Task RecoverAfterFatalErrorAsync(
        CancellationTokenSource recovery,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FatalErrorRecoveryDelayMilliseconds, cancellationToken).ConfigureAwait(false);

            bool started;
            lock (lifecycleLock)
            {
                if (cancellationToken.IsCancellationRequested ||
                    !desiredRunning ||
                    !ReferenceEquals(recoveryCancellation, recovery))
                {
                    return;
                }

                recoveryCancellation = null;
                started = StartServerLocked();
            }

            if (started)
            {
                Logger.Log(LogLevel.Info, "Overlay WebSocket server recovered with a new listener.");
            }

            OnStateChanged?.Invoke(this, new StateChangedArgs(started, !started));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            lock (lifecycleLock)
            {
                Failed = true;
                LastException = e;
            }
            Logger.Log(LogLevel.Error, $"Overlay WebSocket server recovery failed: {e}");
            OnStateChanged?.Invoke(this, new StateChangedArgs(false, true));
        }
        finally
        {
            lock (lifecycleLock)
            {
                if (ReferenceEquals(recoveryCancellation, recovery))
                {
                    recoveryCancellation = null;
                }
            }
            recovery.Dispose();
        }
    }

    private void StopServerLocked(OverlayServer? server)
    {
        if (server?.IsStarted != true)
        {
            return;
        }

        try
        {
            server.Stop();
        }
        catch (Exception e)
        {
            LastException = e;
            Logger.Log(LogLevel.Error, Resources.WSShutdownError, e);
        }
    }

    private void CancelRecoveryLocked()
    {
        var recovery = recoveryCancellation;
        recoveryCancellation = null;
        recovery?.Cancel();
    }

    public string GetModernUrl(string url)
    {
        if (url.Contains("?"))
            url += "&";
        else
            url += "?";

        url += "OVERLAY_WS=ws";
        if (Config.WSServerSSL) url += "s";
        url += "://";
        if (Config.WSServerIP == "*" || Config.WSServerIP == "0.0.0.0")
            url += "127.0.0.1";
        else
            url += Config.WSServerIP;

        url += ":" + Config.WSServerPort + "/ws";
        return url;
    }

    public string GetCertPath()
    {
        var path = Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName,
            "Config",
            "OverlayPluginSSL.p12");

        return path;
    }
    
    public class StateChangedArgs : EventArgs
    {
        public StateChangedArgs(bool running, bool failed)
        {
            this.Running = running;
            this.Failed = failed;
        }

        public bool Running { get; private set; }
        public bool Failed { get; private set; }
    }
}
