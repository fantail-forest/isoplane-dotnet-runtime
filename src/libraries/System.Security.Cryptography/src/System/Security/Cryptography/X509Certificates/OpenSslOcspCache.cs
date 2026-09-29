// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>
    /// The in-memory stand-in for the OCSP response directory on a platform that persists
    /// nothing. Entries are the downloaded DER responses, keyed the way the native cache names
    /// its files (issuer name hash, subject name hash), and carry no freshness rule of their
    /// own: a cached response is re-verified, and its cacheability re-judged, on every use.
    /// </summary>
    internal static class OpenSslOcspCache
    {
        // Responses are a few hundred bytes to a few kilobytes; a chain that outgrows this
        // simply downloads again.
        private const int MaxResponses = 30;
        private const long MaxTotalBytes = 256 * 1024;

        private static readonly BoundedMruCache<byte[]> s_cache =
            new(MaxResponses, MaxTotalBytes, static der => der.Length, onEvict: null);

        internal static string GetKey(SafeX509Handle subject)
        {
            // X509_issuer_name_hash only sets 32 bits worth of data.
            ulong issuerHashLong = Interop.Crypto.X509IssuerNameHash(subject);

            if (issuerHashLong == 0)
            {
                Interop.Crypto.ErrClearError();
            }

            uint issuerHash = unchecked((uint)issuerHashLong);

            // The native cache uses X509_subject_name_hash for the second component; that is not
            // exported to managed code, so hash the subject name's encoding instead. Only
            // consistency matters, not the function.
            byte[] subjectName = Interop.Crypto.LoadX500Name(Interop.Crypto.X509GetSubjectName(subject)).RawData;
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(subjectName, hash);
            uint subjectHash = MemoryMarshal.Read<uint>(hash);

            return $"{issuerHash:x8}.{subjectHash:x8}";
        }

        internal static bool TryGet(string key, [NotNullWhen(true)] out byte[]? der)
        {
            return s_cache.TryGetValue(key, out der);
        }

        internal static void Add(string key, byte[] der)
        {
            s_cache.AddOrUpdate(key, der);
        }

        internal static void Evict(string key)
        {
            s_cache.Remove(key);
        }
    }
}
