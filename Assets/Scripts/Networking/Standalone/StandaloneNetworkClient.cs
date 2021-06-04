

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Extensions;

public class StandaloneNetworkClient : NetworkClient
{
    IPAddress ipAddress;
    TcpClient client;
    NetworkStream stream;

    public override bool Connected => client.Connected;

    public StandaloneNetworkClient(string ip, int port, bool dns = false) : base(ip, port)
    {
        ipAddress = dns ? Dns.GetHostAddresses(ip).First() : IPAddress.Parse(ip);

        client = new TcpClient(ipAddress.AddressFamily);
    }

    public StandaloneNetworkClient(TcpClient client) : base("NIL", 0)
    {
        this.client = client;
        this.stream = client.GetStream();
    }

    public override void BeginConnect(System.Action<IAsyncResult> callback, object state)
    {
        client.BeginConnect(ipAddress, port, new AsyncCallback(callback), state);
    }

    public override void EndConnect(IAsyncResult asyncResult)
    {
        client.EndConnect(asyncResult);
        stream = client.GetStream();
    }

    public override void BeginRead(byte[] buffer, int offset, int size, Action<IAsyncResult> callback, object state)
    {
        stream.BeginRead(buffer, offset, size, new AsyncCallback(callback), state);
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return stream.EndRead(asyncResult);
    }

    public override void Write(byte[] data, int offset, int length)
    {
        stream.Write(data, offset, length);
    }

    public override string IP()
    {
        return client.IP();
    }

    public override void Dispose()
    {
        stream?.Close();
        client?.Close();
    }
}