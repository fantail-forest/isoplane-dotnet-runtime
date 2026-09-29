// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace System.Security.Cryptography.X509Certificates
{
    internal static partial class ChainPal
    {
#pragma warning disable IDE0060
        internal static partial IChainPal FromHandle(IntPtr chainContext)
        {
            throw new PlatformNotSupportedException(SR.SystemSecurityCryptographyX509Certificates_PlatformNotSupported);
        }

        internal static partial bool ReleaseSafeX509ChainHandle(IntPtr handle)
        {
            throw new PlatformNotSupportedException(SR.SystemSecurityCryptographyX509Certificates_PlatformNotSupported);
        }

        internal static partial IChainPal? BuildChain(
            bool useMachineContext,
            ICertificatePal cert,
            X509Certificate2Collection? extraStore,
            OidCollection? applicationPolicy,
            OidCollection? certificatePolicy,
            X509RevocationMode revocationMode,
            X509RevocationFlag revocationFlag,
            X509Certificate2Collection? customTrustStore,
            X509ChainTrustMode trustMode,
            DateTime verificationTime,
            TimeSpan timeout,
            bool disableAia)
        {
            throw new PlatformNotSupportedException(SR.SystemSecurityCryptographyX509Certificates_PlatformNotSupported);
        }

        internal static partial ValueTask<IChainPal?> BuildChainAsync(
            bool useMachineContext,
            ICertificatePal cert,
            X509Certificate2Collection? extraStore,
            OidCollection? applicationPolicy,
            OidCollection? certificatePolicy,
            X509RevocationMode revocationMode,
            X509RevocationFlag revocationFlag,
            X509Certificate2Collection? customTrustStore,
            X509ChainTrustMode trustMode,
            DateTime verificationTime,
            TimeSpan timeout,
            bool disableAia,
            CancellationToken cancellationToken)
        {
            throw new PlatformNotSupportedException(SR.SystemSecurityCryptographyX509Certificates_PlatformNotSupported);
        }
#pragma warning restore IDE0060
    }
}
