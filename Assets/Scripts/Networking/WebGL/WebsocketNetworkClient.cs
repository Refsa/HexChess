using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WebSocketSharp;

public class WebGLNetworkClient : NetworkClient
{
    string wsAddress;
    WebSocket webSocket;
    bool connected;

    #region State
    Action<IAsyncResult> onConnectedCallback;
    object state;

    ConcurrentQueue<byte[]> incoming;
    Task awaitMessageTask;
    #endregion

    public override bool Connected => connected;

    public WebGLNetworkClient(string ip, int port) : base(ip, port)
    {
        wsAddress = $"ws://{ip}:{port}";
        incoming = new ConcurrentQueue<byte[]>();
    }

    public override void BeginConnect(Action<IAsyncResult> asyncCallback, object state)
    {
        this.state = state;
        onConnectedCallback = asyncCallback;

        webSocket = new WebSocket(wsAddress);

        webSocket.OnOpen += OnWebsocketOpen;
        webSocket.OnClose += OnWebsocketClose;
        webSocket.OnMessage += OnWebsocketMessage;
        webSocket.OnError += OnWebsocketError;

        webSocket.ConnectAsync();
    }

    public override void EndConnect(IAsyncResult asyncResult)
    {
        onConnectedCallback = null;
    }

    public override void BeginRead(byte[] buffer, int offset, int size, Action<IAsyncResult> callback, object state)
    {
        awaitMessageTask = Task.Factory.StartNew(() =>
        {
            while(incoming.Count == 0)
            {
                // TODO: bad, I'm lazy
                System.Threading.Thread.Sleep(1);
            }

            if (incoming.TryDequeue(out buffer))
            {
                callback?.Invoke(null);
            }
        });
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] data, int offset, int length)
    {
        throw new NotImplementedException();
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