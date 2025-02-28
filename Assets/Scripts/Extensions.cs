﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Extensions
{
    public static class Extensions 
    {
        public static float Sqr(this int value) => value * value;
        public static float Sqr(this float value) => value * value;
        public static float Sqrt(this float value) => Mathf.Sqrt(value);
        public static int Floor(this float val) => Mathf.FloorToInt(val);
        public static int Ceil(this float val) => Mathf.CeilToInt(val);

        public static void ForEach<T>(this IEnumerable<T> source, System.Action<T> action)
        {
            foreach(T item in source)
                action(item);
        }

        public static int BoolToInt(this bool val) => val ? 1 : 0;
        public static bool IntToBool(this int val) => val == 0 ? false : true;

        public static string IP(this TcpClient client) => $"{((IPEndPoint)client?.Client.RemoteEndPoint).Address}";

        public static string GetStringFromSeconds(this float seconds) => seconds < 60 
            ? @"%s\.f" 
            : seconds < 3600
                ? @"%m\:%s\.f"
                : @"%h\:%m\:%s\.f";

        public static bool TryGetComponentInChildren<T>(this GameObject parent, out T component) where T : Component
        {
            component = parent.GetComponentInChildren<T>();
            return component != null;
        }
    }
}