using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WebSocketSharp;

public class WebsocketNetworkClient : NetworkClient
{
    string wsAddress;
    WebSocket webSocket;
    bool connected;

    #region State
    Action<IAsyncResult> onConnectedCallback;
    object state;
    bool isServer;

    ConcurrentQueue<byte[]> incoming;
    int lastReadBytes;
    #endregion

    public override bool Connected => connected;

    public WebsocketNetworkClient(string ip, int port) : base(ip, port)
    {
        wsAddress = $"ws://127.0.0.1/relay";

        incoming = new ConcurrentQueue<byte[]>();
    }

    public WebsocketNetworkClient(string ip, int port, bool isServer = false) : this(ip, port)
    {
        this.isServer = isServer;
    }

    public override void BeginConnect(Action<IAsyncResult> asyncCallback, object state)
    {
        this.state = state;
        onConnectedCallback = asyncCallback;

        string room = "";

        Task.Factory.StartNew(() =>
        {
            // TODO: more bad, more lazy
            using (var discover = new WebSocket(wsAddress))
            {
                discover.OnMessage += (sender, args) =>
                {
                    if (args.Data != "NIL")
                    {
                        room = args.Data;
                    }
                };

                discover.Connect();

                if (isServer)
                {
                    discover.Send("SERVER:" + ip);
                }
                else
                {
                    discover.Send(Networker.GetPublicIPAddress());
                }

                while (string.IsNullOrEmpty(room))
                {
                    discover.Send(ip);
                    System.Threading.Thread.Sleep(10);
                }
            }

            webSocket = new WebSocket(wsAddress + room);

            webSocket.OnOpen += OnWebsocketOpen;
            webSocket.OnClose += OnWebsocketClose;
            webSocket.OnMessage += OnWebsocketMessage;
            webSocket.OnError += OnWebsocketError;

            webSocket.ConnectAsync();
        });
    }

    public override void EndConnect(IAsyncResult asyncResult)
    {
        onConnectedCallback = null;
    }

    public override void BeginRead(byte[] buffer, int offset, int size, Action<IAsyncResult> callback, object state)
    {
        Task.Factory.StartNew(() =>
        {
            while (incoming.Count == 0)
            {
                // TODO: bad, I'm lazy
                System.Threading.Thread.Sleep(1);
            }

            if (incoming.TryDequeue(out var data))
            {
                lastReadBytes = data.Length;
                Debug.Log(lastReadBytes + " bytes through WebSocket");

                System.Buffer.BlockCopy(data, 0, buffer, offset, lastReadBytes);
                callback?.Invoke(null);
            }
        });
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return lastReadBytes;
    }

    public override void Write(byte[] data, int offset, int length)
    {
        try
        {
            webSocket.Send(data);
        }
        catch
        {
            throw new System.IO.IOException();
        }
    }

    public override void Close()
    {
        webSocket.Close();
    }

    public override void Dispose()
    {
        webSocket.Close();
        connected = false;
        webSocket = null;
    }

    #region Websocket API
    private void OnWebsocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogException(e.Exception);
    }

    private void OnWebsocketMessage(object sender, MessageEventArgs e)
    {
        incoming.Enqueue(e.RawData);
    }

    private void OnWebsocketClose(object sender, CloseEventArgs e)
    {
        connected = false;
    }

    private void OnWebsocketOpen(object sender, EventArgs e)
    {
        connected = true;
        onConnectedCallback?.Invoke(null);
    }
    #endregion
}