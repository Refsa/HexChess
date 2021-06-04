

using System;
using System.Threading.Tasks;

public class WebsocketNetworkServer : NetworkServer
{
    public WebsocketNetworkServer(int port) : base(port)
    {
    }

    WebsocketNetworkClient server;

    public override void BeginAcceptTcpClient(Action<IAsyncResult> callback, object state)
    {
        Task.Factory.StartNew(() =>
        {
            server = new WebsocketNetworkClient(Networker.GetPublicIPAddress(), port, true);
            server.BeginConnect(null, null);

            while (!server.Connected)
            {
                // TODO: me lazy
                System.Threading.Thread.Sleep(10);
            }

            callback.Invoke(null);
        });
    }

    public override NetworkClient EndAcceptTcpClient(IAsyncResult asyncResult)
    {
        return server;
    }

    public override void Start()
    {

    }

    public override void Stop()
    {

    }

    public override void Dispose()
    {

    }
}