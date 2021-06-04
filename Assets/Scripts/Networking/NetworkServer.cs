

using System;

public abstract class NetworkServer : System.IDisposable
{
    protected int port;

    public NetworkServer(int port)
    {
        this.port = port;
    }

    public abstract void Start();
    public abstract void Stop();

    public abstract void BeginAcceptTcpClient(System.Action<IAsyncResult> callback, object state);
    public abstract NetworkClient EndAcceptTcpClient(IAsyncResult asyncResult);


    public abstract void Dispose();
}