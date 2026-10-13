// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// VERIFIER: adversarial tests for the 3.18 xref-offset recovery scan.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class XrefRecoveryDecoyVerifierTests
{
    // When object 4's xref offset is stale, ScanForObject takes the LAST
    // parseable "4 0 obj" in the file. A decoy definition inside a LATER
    // stream body then shadows the real (earlier) definition.
    [Fact]
    public void StaleOffset_DecoyInsideLaterStream_ShadowsRealObject()
    {
        // Object 4 is the page's content stream. Object 5 is an unrelated
        // stream whose BODY contains a fake "4 0 obj ... endobj" with
        // different content. Object 4's xref offset is corrupted.
        byte[] realContent = Encoding.Latin1.GetBytes("BT /F1 12 Tf (REAL) Tj ET");
        string decoyBody =
            "4 0 obj <</Length 26>>\nstream\nBT /F1 12 Tf (DECOY) Tj ET\nendstream endobj";

        using MemoryStream ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        W("%PDF-1.7\n");
        List<long> offsets = new List<long>();

        offsets.Add(ms.Length);
        W("1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n");
        offsets.Add(ms.Length);
        W("2 0 obj <</Type/Pages/Kids[3 0 R]/Count 1>> endobj\n");
        offsets.Add(ms.Length);
        W("3 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
            + "/Resources<<>>/Contents 4 0 R>> endobj\n");

        offsets.Add(ms.Length); // real object 4
        W("4 0 obj <</Length " + realContent.Length + ">>\nstream\n");
        ms.Write(realContent);
        W("\nendstream endobj\n");

        offsets.Add(ms.Length); // object 5 carrying the decoy in its body
        W("5 0 obj <</Length " + decoyBody.Length + ">>\nstream\n"
            + decoyBody + "\nendstream endobj\n");

        long xref = ms.Length;
        W("xref\n0 6\n0000000000 65535 f \n");
        for (int i = 0; i < offsets.Count; i++)
        {
            long off = offsets[i];
            if (i == 3)
            {
                off = 2; // corrupt object 4's offset (points into the header)
            }

            W(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        W("trailer <</Size 6/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");

        ms.Position = 0;
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);
        PdfPrimitive contents = doc.Objects.Resolve(
            doc.Pages[0].Dictionary[PdfName.Intern("Contents")]);

        string body = contents is PdfStream s
            ? Encoding.Latin1.GetString(s.RawBytes)
            : "(not a stream)";

        Assert.False(body.Contains("DECOY", StringComparison.Ordinal),
            "xref recovery resolved object 4 to the decoy definition planted "
            + "inside object 5's stream body instead of the real, earlier one");
        Assert.Contains("REAL", body, StringComparison.Ordinal);
    }
}
