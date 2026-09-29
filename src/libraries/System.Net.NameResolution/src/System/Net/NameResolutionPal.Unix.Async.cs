// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net
{
    // Unix resolves names synchronously on the thread pool; the asynchronous
    // entry points exist for the platforms that have a native async lookup.
    internal static partial class NameResolutionPal
    {
        public const bool SupportsGetAddrInfoAsync = false;

        public static bool SupportsGetNameInfo => true;

#pragma warning disable IDE0060
        internal static Task? GetAddrInfoAsync(string hostName, bool justAddresses, AddressFamily family, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
#pragma warning restore IDE0060
    }
}
