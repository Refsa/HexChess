using System;
using System.Net;

public abstract class NetworkClient : System.IDisposable
{
    protected string ip;
    protected int port;

    public event System.Action OnConnected;
    public event System.Action OnDisconnected;
    public event System.Action<byte[], int> OnMessage;

    public abstract bool Connected { get; }

    public NetworkClient(string ip, int port)
    {
        this.ip = ip;
        this.port = port;
    }

    public virtual string IP()
    {
        return $"{ip}:{port}";
    }

    public abstract void BeginConnect(System.Action<IAsyncResult> asyncCallback, object state);
    public abstract void EndConnect(IAsyncResult asyncResult);

    public abstract void BeginRead(byte[] buffer, int offset, int size, System.Action<IAsyncResult> callback, object state);
    public abstract int EndRead(IAsyncResult asyncResult);

    public abstract void Write(byte[] data, int offset, int length);


    public abstract void Close();
    public abstract void Dispose();

    protected void MessageReceived(byte[] data, int length)
    {
        OnMessage?.Invoke(data, length);
    }
}