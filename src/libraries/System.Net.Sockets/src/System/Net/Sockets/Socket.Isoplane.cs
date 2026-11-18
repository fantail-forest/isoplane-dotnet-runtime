// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Net.Sockets
{
    public partial class Socket
    {
        // Isoplane cannot save an open connection in a template. While it prepares one, each socket
        // keeps where it was opened, so a socket left open can be reported with its origin. Only the
        // Isoplane SDK starts a recording, and its guests run on one thread.
        private static Dictionary<SafeSocketHandle, (Socket Socket, StackTrace Origin)>? s_isoplaneOrigins;

        internal static void RecordIsoplaneSocketOrigins() => s_isoplaneOrigins = new();

        /// <summary>Ends the recording and returns each socket opened during it that is still open, with where it was opened.</summary>
        internal static KeyValuePair<Socket, StackTrace>[] TakeIsoplaneOpenSockets()
        {
            Dictionary<SafeSocketHandle, (Socket Socket, StackTrace Origin)>? origins = s_isoplaneOrigins;
            s_isoplaneOrigins = null;
            if (origins is null)
            {
                return [];
            }

            var open = new KeyValuePair<Socket, StackTrace>[origins.Count];
            int index = 0;
            foreach ((Socket socket, StackTrace origin) in origins.Values)
            {
                open[index++] = new(socket, origin);
            }

            return open;
        }

        private void RememberIsoplaneOrigin() => s_isoplaneOrigins?.Add(_handle, (this, new StackTrace(skipFrames: 1, fNeedFileInfo: false)));

        internal static void ForgetIsoplaneOrigin(SafeSocketHandle handle) => s_isoplaneOrigins?.Remove(handle);
    }
}
