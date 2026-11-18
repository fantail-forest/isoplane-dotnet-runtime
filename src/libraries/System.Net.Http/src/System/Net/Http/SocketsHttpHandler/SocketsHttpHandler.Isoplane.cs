// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace System.Net.Http
{
    public sealed partial class SocketsHttpHandler
    {
        // A pool opens its connections on a background task, so where a socket was opened says
        // nothing about the application. While Isoplane prepares a template, each handler keeps
        // where it was created, and each socket a pool opens is tied to its handler.
        private static readonly ConditionalWeakTable<Socket, StackTrace> s_isoplaneOwners = new();
        private static bool s_isoplaneRecording;

        internal static void RecordIsoplaneConnectionOwners(bool recording) => s_isoplaneRecording = recording;

        /// <summary>Where the handler whose pool opened <paramref name="socket"/> was created, if that was recorded.</summary>
        internal static StackTrace? IsoplaneConnectionOwner(Socket socket) =>
            s_isoplaneOwners.TryGetValue(socket, out StackTrace? owner) ? owner : null;

        internal static StackTrace? IsoplaneHandlerOrigin() =>
            s_isoplaneRecording ? new StackTrace(skipFrames: 2, fNeedFileInfo: false) : null;

        internal static void RememberIsoplaneOwner(Socket socket, StackTrace? owner)
        {
            if (owner is not null)
            {
                s_isoplaneOwners.Add(socket, owner);
            }
        }
    }
}
