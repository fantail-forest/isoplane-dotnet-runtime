// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Win32.SafeHandles;

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>
    /// The in-memory stand-in for the user's intermediate certificate store on a platform that
    /// persists nothing: issuers fetched through Authority Information Access are kept so later
    /// chains find them without another download.
    /// </summary>
    internal static class OpenSslIssuerCache
    {
        private const int MaxIssuers = 30;

        private static readonly BoundedMruCache<SafeX509Handle> s_cache =
            new(MaxIssuers, long.MaxValue, sizeOf: null, onEvict: static handle => handle.Dispose());

        internal static void Add(X509Certificate2 certificate)
        {
            string key = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
            s_cache.AddOrUpdate(key, Interop.Crypto.X509UpRef(certificate.Handle));
        }

        /// <summary>Pushes an up-referenced copy of every cached issuer onto <paramref name="untrusted"/>.</summary>
        internal static void AddToStack(SafeX509StackHandle untrusted)
        {
            s_cache.ForEachValue(untrusted, static (stack, issuer) => OpenSslX509ChainProcessor.AddToStackAndUpRef(issuer, stack));
        }
    }
}
