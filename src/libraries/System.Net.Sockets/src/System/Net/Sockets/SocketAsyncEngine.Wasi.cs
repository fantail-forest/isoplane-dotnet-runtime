// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Interop;
using static Interop.Sys;

namespace System.Net.Sockets
{
    internal sealed unsafe class SocketAsyncEngine
    {
        internal const bool InlineSocketCompletionsEnabled = true;
        private static readonly SocketAsyncEngine s_engine = new SocketAsyncEngine();

        // A socket contributes at most its own pollable while connecting or
        // listening, or an input and an output pollable once connected.
        private const int MaxPollablesPerSocket = 4;

        public static bool TryRegisterSocket(IntPtr socketHandle, SocketAsyncContext context, out SocketAsyncEngine? engine, out Interop.Error error)
        {
            engine = s_engine;

            RegisterWasiPollHook(context, BeforePollHook, HandleSocketEvent, context.unregisterPollHook.Token);
            error = Interop.Error.SUCCESS;
            return true;
        }

        public static void UnregisterSocket(SocketAsyncContext context)
        {
            context.unregisterPollHook.Cancel();
        }

        // Asks libc which pollables stand for this socket's readiness, and
        // dispatches immediately for a socket libc already reports as ready
        // (a failed connection), which has no pollable to wait on.
        public static IList<int> BeforePollHook(object? state)
        {
            var context = (SocketAsyncContext)state!;
            if (context._socket.IsClosed)
            {
                return [];
            }

            uint* handles = stackalloc uint[MaxPollablesPerSocket];
            int count;
            Sys.SocketEvents events;
            IntPtr socketHandle = context._socket.DangerousGetHandle();
            Interop.Error error = Interop.Sys.GetWasiSocketReadiness(socketHandle, handles, MaxPollablesPerSocket, &count, &events);
            if (error == Interop.Error.EBADF)
            {
                // closed between the IsClosed check and the query, or never a socket
                return [];
            }
            if (error != Interop.Error.SUCCESS)
            {
                Environment.FailFast("Can't determine readiness of socket handle " + socketHandle + ": " + error);
            }

            if (events != Sys.SocketEvents.None)
            {
                context.HandleEventsInline(events);
            }

            var pollableHandles = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                pollableHandles.Add((int)handles[i]);
            }
            return pollableHandles;
        }

        public static void HandleSocketEvent(object? state)
        {
            SocketAsyncContext ctx = (SocketAsyncContext)state!;
            try
            {
                using (ExecutionContext.SuppressFlow())
                {
                    ctx.HandleEventsInline(Sys.SocketEvents.Write | Sys.SocketEvents.Read);
                }
            }
            catch (Exception e)
            {
                Environment.FailFast("Exception thrown from SocketAsyncEngine event loop: " + e.ToString(), e);
            }
        }

        private static void RegisterWasiPollHook(object? state, Func<object?, IList<int>> beforePollHook, Action<object?> onResolveCallback, CancellationToken cancellationToken)
        {
            CallRegisterWasiPollHook((Thread)null!, state, beforePollHook, onResolveCallback, cancellationToken);

            [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RegisterWasiPollHook")]
            static extern void CallRegisterWasiPollHook(Thread t, object? state, Func<object?, IList<int>> beforePollHook, Action<object?> onResolveCallback, CancellationToken cancellationToken);
        }
    }
}
