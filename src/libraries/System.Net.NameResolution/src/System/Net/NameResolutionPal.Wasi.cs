// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net
{
    // WASI has no thread to block in getaddrinfo on, and its name lookup is a
    // resource whose pollable the event loop can await; forward lookups
    // therefore always take the asynchronous path. The synchronous entry
    // points would have to block inside the call and are unsupported, as is
    // the reverse lookup WASI has no interface for. Literals are answered in
    // the guest, and the host name comes from libc.
    internal static partial class NameResolutionPal
    {
        public const bool SupportsGetAddrInfoAsync = true;

        public static bool SupportsGetNameInfo => true;

        internal static Task? GetAddrInfoAsync(string hostName, bool justAddresses, AddressFamily family, CancellationToken cancellationToken)
        {
            return justAddresses
                ? ResolveAddressesAsync(hostName, family, cancellationToken)
                : ResolveHostEntryAsync(hostName, family, cancellationToken);
        }

        public static SocketError TryGetAddrInfo(string name, bool justAddresses, AddressFamily addressFamily, out string? hostName, out string[] aliases, out IPAddress[] addresses, out int nativeErrorCode)
        {
            throw new PlatformNotSupportedException(SR.net_wasi_sync_name_resolution);
        }

        public static string? TryGetNameInfo(IPAddress addr, out SocketError socketError, out int nativeErrorCode)
        {
            throw new PlatformNotSupportedException();
        }

        public static string GetHostName() => Interop.Sys.GetHostName();

        private static async Task<IPHostEntry> ResolveHostEntryAsync(string hostName, AddressFamily family, CancellationToken cancellationToken)
        {
            IPAddress[] addresses = await ResolveAddressesAsync(hostName, family, cancellationToken).ConfigureAwait(false);
            return new IPHostEntry
            {
                HostName = hostName,
                Aliases = Array.Empty<string>(),
                AddressList = addresses,
            };
        }

        private static async Task<IPAddress[]> ResolveAddressesAsync(string hostName, AddressFamily family, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (int stream, int pollable) = StartLookup(hostName);
            try
            {
                await RegisterWasiPollableHandle((Thread)null!, pollable, true, cancellationToken).ConfigureAwait(false);
                var addresses = new List<IPAddress>();
                while (NextAddress(stream) is IPAddress address)
                {
                    if (family == AddressFamily.Unspecified || family == address.AddressFamily)
                    {
                        addresses.Add(address);
                    }
                }
                return addresses.ToArray();
            }
            finally
            {
                Interop.Sys.WasiResolveAddressesDrop(stream);
            }
        }

        private static unsafe (int Stream, int Pollable) StartLookup(string hostName)
        {
            byte[] name = Encoding.UTF8.GetBytes(hostName);
            int stream;
            int pollable;
            int error;
            fixed (byte* pointer = name)
            {
                error = Interop.Sys.WasiResolveAddressesStart(pointer, name.Length, &stream, &pollable);
            }
            if (error != 0)
            {
                throw new SocketException((int)GetSocketErrorForWasiError(error - 1));
            }
            return (stream, pollable);
        }

        private static unsafe IPAddress? NextAddress(int stream)
        {
            byte* bytes = stackalloc byte[16];
            int length = Interop.Sys.WasiResolveNextAddress(stream, bytes, 16);
            if (length < 0)
            {
                throw new SocketException((int)GetSocketErrorForWasiError(-length - 1));
            }
            return length == 0 ? null : new IPAddress(new ReadOnlySpan<byte>(bytes, length));
        }

        // wasi:sockets/network.error-code, in declaration order.
        private static SocketError GetSocketErrorForWasiError(int error) => error switch
        {
            1 => SocketError.AccessDenied,             // access-denied
            2 => SocketError.OperationNotSupported,    // not-supported
            3 => SocketError.InvalidArgument,          // invalid-argument
            4 => SocketError.NoBufferSpaceAvailable,   // out-of-memory
            5 => SocketError.TimedOut,                 // timeout
            10 => SocketError.NoBufferSpaceAvailable,  // new-socket-limit
            18 => SocketError.HostNotFound,            // name-unresolvable
            19 => SocketError.TryAgain,                // temporary-resolver-failure
            20 => SocketError.NoRecovery,              // permanent-resolver-failure
            _ => SocketError.SocketError,
        };

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RegisterWasiPollableHandle")]
        private static extern Task RegisterWasiPollableHandle(Thread t, int handle, bool ownsPollable, CancellationToken cancellationToken);
    }
}
