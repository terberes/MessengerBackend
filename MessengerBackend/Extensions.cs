﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DerConverter.Asn.KnownTypes;
using Microsoft.Extensions.Logging;

namespace MessengerBackend
{
    public static class Extensions
    {
        public static string GetString(this Stream s)
        {
            using var reader = new StreamReader(s);
            return reader.ReadToEnd();
        }

        public static string RemoveWhitespace(this string input) => input.Replace(" ", "");

        public static byte[] ToByteArray(this DerAsnBitString bitString) => bitString.Encode(null).Skip(1).ToArray();

        public static bool EqualsAnyString(this string self, params string[] args) =>
            args.Any(arg => arg.Equals(self));

        public static async Task<(WebSocketReceiveResult, byte[])> ReceiveFrameAsync(
            this WebSocket socket, CancellationToken cancelToken)
        {
            WebSocketReceiveResult response;
            var message = new List<byte>();

            var buffer = new byte[4096];
            do
            {
                response = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancelToken);
                message.AddRange(new ArraySegment<byte>(buffer, 0, response.Count));
            } while (!response.EndOfMessage);

            return (response, message.ToArray());
        }

        // Obtain the custom attribute for the method. 
        // The value returned contains the StateMachineType property. 
        // Null is returned if the attribute isn't present for the method. 
        public static bool IsAsync(this MethodInfo methodInfo) =>
            methodInfo.GetCustomAttribute(
                typeof(AsyncStateMachineAttribute)) is AsyncStateMachineAttribute;

        public static TimeSpan MeasureSpeed(this Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed;
        }

        public static IDisposable BeginNamedScope(this ILogger logger,
            string name, params ValueTuple<string, object>[] properties)
        {
            var dictionary = properties.ToDictionary(p => p.Item1, p => p.Item2);
            return logger.BeginScope(dictionary);
        }
    }
}