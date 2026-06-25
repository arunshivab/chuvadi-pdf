// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 (file structure)
// Tests for LA-32: PdfDocument.Save must serialize the content streams of an
// opened (lazily-loaded) document, not just the materialized objects.

using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class PdfDocumentSaveContentTests
{
    // A page whose content stream (object 4, "...10 10 50 50 re f") is a
    // separate indirect object — lazily loaded on open, so a naive Save that
    // only writes materialized objects would drop it.
    private static byte[] ContentBearingPdf()
    {
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>",
            "<</Length 25>>\nstream\n1 0 0 rg 10 10 50 50 re f\nendstream",
        };

        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        long[] offsets = new long[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets[i] = ms.Length;
            Write((i + 1) + " 0 obj ");
            Write(bodies[i]);
            Write(" endobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Length + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10") + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Length + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }

    [Fact]
    public void Save_OpenedDocument_PreservesPageContentStream()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(ContentBearingPdf()), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        source.Save(output);

        // The uncompressed content operators must survive the round-trip.
        string saved = Encoding.Latin1.GetString(output.ToArray());
        saved.Should().Contain("10 10 50 50 re");
    }

    [Fact]
    public void Save_OpenedDocument_ContentResolvesOnReopen()
    {
        byte[] saved;
        using (PdfDocument source = PdfDocument.Open(new MemoryStream(ContentBearingPdf()), leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            source.Save(output);
            saved = output.ToArray();
        }

        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(saved), leaveOpen: true);
        reopened.PageCount.Should().Be(1);

        PdfPrimitive? contents = reopened.Pages[0].Contents;
        contents.Should().NotBeNull();
        PdfStream stream = (PdfStream)reopened.Objects.Resolve(contents!);
        Encoding.Latin1.GetString(stream.RawBytes).Should().Contain("re");
    }

    [Fact]
    public void Save_OpenedDocument_Encrypted_PreservesContent()
    {
        byte[] encrypted;
        using (PdfDocument source = PdfDocument.Open(new MemoryStream(ContentBearingPdf()), leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            source.Save(output, EncryptionOptions.Aes256("la32-pw"));
            encrypted = output.ToArray();
        }

        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(encrypted), "la32-pw", leaveOpen: true);
        reopened.PageCount.Should().Be(1);

        PdfStream stream = (PdfStream)reopened.Objects.Resolve(reopened.Pages[0].Contents!);
        Encoding.Latin1.GetString(stream.RawBytes).Should().Contain("re");
    }
}
