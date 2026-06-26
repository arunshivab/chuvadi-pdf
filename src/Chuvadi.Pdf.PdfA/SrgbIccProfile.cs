// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 3 — PDF/A structural metadata
//
// Loads the bundled default sRGB output profile. The embedded file is the
// "sRGB IEC61966-2.1" ICC v2 profile created by Graeme W. Gill and released into
// the public domain (the ArgyllCMS sRGB profile); ICC v2 satisfies both PDF/A-1
// and PDF/A-2. Callers may supply their own profile via PdfAOptions.

using System;
using System.IO;
using System.Reflection;

namespace Chuvadi.Pdf.PdfA;

internal static class SrgbIccProfile
{
    private const string ResourceName = "Chuvadi.Pdf.PdfA.Resources.sRGB-IEC61966-2.1.icc";

    /// <summary>Loads the bundled public-domain sRGB ICC v2 profile bytes.</summary>
    /// <returns>The ICC profile bytes.</returns>
    /// <exception cref="InvalidOperationException">The embedded profile is missing.</exception>
    internal static byte[] Load()
    {
        Assembly assembly = typeof(SrgbIccProfile).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The bundled sRGB ICC profile resource is missing.");
        using MemoryStream buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
