// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static partial class Sys
    {
        // The three steps of a wasi:sockets/ip-name-lookup resolve-address-stream:
        // start a lookup and get the stream and the pollable to await, read the
        // next address (4 or 16 bytes, 0 at the end), and drop the stream.
        // A failure is the WASI error code plus one, negated by the reader.

        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_WasiResolveAddressesStart")]
        internal static unsafe partial int WasiResolveAddressesStart(byte* name, int length, int* stream, int* pollable);

        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_WasiResolveNextAddress")]
        internal static unsafe partial int WasiResolveNextAddress(int stream, byte* bytes, int capacity);

        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_WasiResolveAddressesDrop")]
        internal static partial void WasiResolveAddressesDrop(int stream);
    }
}
