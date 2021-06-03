using System;
using UnityEngine;
using NativeWebSocket;

public class WebGLNetworkClient : NetworkClient
{
    string wsAddress;
    // WebSocket webSocket;

    AsyncCallback onConnectedCallback;

    public WebGLNetworkClient(string ip, int port) : base(ip, port)
    {
        wsAddress = $"ws://{ip}:{port}";
    }

    /* public void BeginConnect()
    {
        webSocket = new WebSocket(wsAddress);

        webSocket.OnOpen += OnWebsocketOpen;
        webSocket.OnClose += OnWebsocketClose;
        webSocket.OnMessage += OnWebsocketMessage;
        webSocket.OnError += OnWebsocketError;

        webSocket.ConnectAsync();
    }

    private void OnWebsocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogException(e.Exception);
        Debug.LogError(e.Message);
    }

    private void OnWebsocketMessage(object sender, MessageEventArgs e)
    {

    }

    private void OnWebsocketClose(object sender, CloseEventArgs e)
    {
        Debug.Log($"Websocket Closed: {e.Code} - {e.Reason} - {e.WasClean}");
    }

    private void OnWebsocketOpen(object sender, EventArgs e)
    {
        Debug.Log($"Websocket Connected: {sender}");
    } */
}