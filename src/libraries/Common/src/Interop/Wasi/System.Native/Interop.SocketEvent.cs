// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static partial class Sys
    {
        [Flags]
        internal enum SocketEvents : int
        {
            None = 0x00,
            Read = 0x01,
            Write = 0x02,
            ReadClose = 0x04,
            Close = 0x08,
            Error = 0x10
        }

        /// <summary>
        /// The wasi:io/poll pollables that stand for a socket's readiness, and
        /// the events libc reports as ready already (which have no pollable).
        /// Fails with EBADF when the handle is not an open socket.
        /// </summary>
        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_GetWasiSocketReadiness")]
        internal static unsafe partial Error GetWasiSocketReadiness(IntPtr socket, uint* handles, int capacity, int* count, SocketEvents* events);
    }
}
