using System.Net;

public abstract class NetworkClient
{
    protected string ip;
    protected int port;

    public NetworkClient(string ip, int port)
    {
        this.ip = ip;
        this.port = port;
    }
}