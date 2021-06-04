using System;
using System.Net.Sockets;

public class StandaloneNetworkServer : NetworkServer
{
    TcpListener listener;

    public StandaloneNetworkServer(int port) : base(port)
    {
        listener = TcpListener.Create(port);
    }

    public override void BeginAcceptTcpClient(Action<IAsyncResult> callback, object state)
    {
        listener.BeginAcceptTcpClient(new AsyncCallback(callback), state);
    }

    public override NetworkClient EndAcceptTcpClient(IAsyncResult asyncResult)
    {
        var client = listener.EndAcceptTcpClient(asyncResult);
        return new StandaloneNetworkClient(client);
    }

    public override void Start()
    {
        listener.Start();
    }

    public override void Stop()
    {
        listener.Stop();
    }

    public override void Dispose()
    {
        listener.Stop();
    }
}