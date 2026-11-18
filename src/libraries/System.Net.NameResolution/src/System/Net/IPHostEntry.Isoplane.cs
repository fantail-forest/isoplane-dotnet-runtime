// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Net
{
    public partial class IPHostEntry
    {
        // Isoplane cannot save a lookup that is still in progress in a template. While it prepares
        // one, each lookup keeps the name it resolves and where it was started. These members are
        // on this class because the Isoplane SDK reaches them through a public type of this assembly.
        private static Dictionary<int, KeyValuePair<string, StackTrace>>? s_isoplaneLookups;

        internal static void RecordIsoplaneLookups() => s_isoplaneLookups = new();

        /// <summary>Ends the recording and returns the name and origin of each lookup started during it that has not finished.</summary>
        internal static KeyValuePair<string, StackTrace>[] TakeIsoplanePendingLookups()
        {
            Dictionary<int, KeyValuePair<string, StackTrace>>? lookups = s_isoplaneLookups;
            s_isoplaneLookups = null;
            if (lookups is null)
            {
                return [];
            }

            var pending = new KeyValuePair<string, StackTrace>[lookups.Count];
            lookups.Values.CopyTo(pending, 0);
            return pending;
        }

        internal static void RememberIsoplaneLookup(int stream, string hostName) =>
            s_isoplaneLookups?.Add(stream, new(hostName, new StackTrace(skipFrames: 1, fNeedFileInfo: false)));

        internal static void ForgetIsoplaneLookup(int stream) => s_isoplaneLookups?.Remove(stream);
    }
}
