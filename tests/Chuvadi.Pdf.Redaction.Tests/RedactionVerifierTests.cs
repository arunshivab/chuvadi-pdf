// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.20 (PDF-03): replacement-text leaks in tagged content are
// stripped, and RedactionVerifier / ExtractWithin report what a region paints.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionVerifierTests
{
    private const string Page =
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/StructParents 0/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>";

    private static readonly RectangleF Box = new RectangleF(250, 250, 150, 100);

    // ── replacement-text leaks (hex-aware: PdfWriter may emit strings as hex) ──

    [Fact]
    public void InlineBdcExpansion_IsStripped()
    {
        string output = RedactTagged("/Span <</E (EXPSECRET)>> BDC BT /F1 12 Tf 300 300 Td (ZZZ) Tj ET EMC");

        Leaks(output, "EXPSECRET").Should().BeFalse();
    }

    [Fact]
    public void InlineBdcActualText_IsStripped()
    {
        string output = RedactTagged("/Span <</ActualText (ACTSECRET)>> BDC BT /F1 12 Tf 300 300 Td (ZZZ) Tj ET EMC");

        Leaks(output, "ACTSECRET").Should().BeFalse();
    }

    [Fact]
    public void StructElementExpansion_IsStripped()
    {
        string output = RedactTagged(
            "/Span <</MCID 0>> BDC BT /F1 12 Tf 300 300 Td (WWW) Tj ET EMC",
            "/StructTreeRoot 6 0 R",
            Obj("<</Type/StructTreeRoot/K 7 0 R/ParentTree 8 0 R>>"),
            Obj("<</Type/StructElem/S/Span/P 6 0 R/Pg 3 0 R/K 0/E (STRUCTESECRET)>>"),
            Obj("<</Nums[0 [7 0 R]]>>"));

        Leaks(output, "STRUCTESECRET").Should().BeFalse();
    }

    [Fact]
    public void StructElementActualText_IsStripped()
    {
        string output = RedactTagged(
            "/Span <</MCID 0>> BDC BT /F1 12 Tf 300 300 Td (WWW) Tj ET EMC",
            "/StructTreeRoot 6 0 R",
            Obj("<</Type/StructTreeRoot/K 7 0 R/ParentTree 8 0 R>>"),
            Obj("<</Type/StructElem/S/Span/P 6 0 R/Pg 3 0 R/K 0/ActualText (STRUCTSECRET)>>"),
            Obj("<</Nums[0 [7 0 R]]>>"));

        Leaks(output, "STRUCTSECRET").Should().BeFalse();
    }

    [Fact]
    public void NamedPropertyListActualText_IsStripped()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R/MarkInfo<</Marked true>>>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]"
                + "/Resources<</Font<</F1 5 0 R>>/Properties<</MC0 6 0 R>>>>/Contents 4 0 R>>"),
            Stream("/Span /MC0 BDC BT /F1 12 Tf 300 300 Td (UUU) Tj ET EMC"),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
            Obj("<</ActualText (PROPSECRET)>>"),
        };

        string output = Redact(bodies);

        Leaks(output, "PROPSECRET").Should().BeFalse();
    }

    [Fact]
    public void ReplacementTextOutsideTheBox_IsKept()
    {
        string output = RedactTagged("/Span <</ActualText (KEEPME)>> BDC BT /F1 12 Tf 20 20 Td (AAA) Tj ET EMC");

        Leaks(output, "KEEPME").Should().BeTrue("the marked text is outside the redaction box");
    }

    // ── RedactionVerifier ──

    [Fact]
    public void Verify_OnUnredactedDocument_ReportsTheText()
    {
        using PdfDocument doc = Open(TextPage("BT /F1 12 Tf 300 300 Td (SECRET) Tj ET BT /F1 12 Tf 20 20 Td (PUBLIC) Tj ET"));

        RedactionVerification result = RedactionVerifier.Verify(doc, new[] { new RedactionRect(0, Box) });

        result.IsClean.Should().BeFalse();
        result.SurvivingText.Should().Contain("SECRET");
        result.SurvivingText.Should().NotContain("PUBLIC");
        result.Survivors.Should().OnlyContain(s => s.Kind == RedactionSurvivorKind.Text && s.PageIndex == 0);
    }

    [Fact]
    public void Verify_AfterRedaction_IsClean()
    {
        byte[] source = TextPage("BT /F1 12 Tf 300 300 Td (SECRET) Tj ET BT /F1 12 Tf 20 20 Td (PUBLIC) Tj ET");
        RedactionRect[] rects = { new RedactionRect(0, Box) };

        using PdfDocument redacted = Open(RedactBytes(source, rects));
        RedactionVerification result = RedactionVerifier.Verify(redacted, rects);

        result.IsClean.Should().BeTrue("redaction removed the text and the overlay itself is ignored");
        RedactionVerifier.ExtractWithin(redacted, 0, new RectangleF(10, 10, 100, 30))
            .Where(s => s.Kind == RedactionSurvivorKind.Text)
            .Select(s => s.Text)
            .Should().ContainInOrder("P", "U", "B", "L", "I", "C");
    }

    [Fact]
    public void Verify_ReportsImagesPathsAndAnnotations()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</XObject<</Im0 5 0 R>>>>"
                + "/Annots[6 0 R]/Contents 4 0 R>>"),
            Stream("q 20 0 0 20 300 300 cm /Im0 Do Q 0 0 1 RG 2 w 260 260 m 390 260 l S"),
            Stream("/Type/XObject/Subtype/Image/Width 1/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8", "\u0080"),
            Obj("<</Type/Annot/Subtype/Text/Rect[320 320 340 340]/Contents (NOTE)>>"),
        };
        using PdfDocument doc = Open(Build(bodies));

        RedactionVerification result = RedactionVerifier.Verify(doc, new[] { new RedactionRect(0, Box) });

        result.Survivors.Select(s => s.Kind).Should().Contain(new[]
        {
            RedactionSurvivorKind.Image,
            RedactionSurvivorKind.Path,
            RedactionSurvivorKind.Annotation,
        });
        result.Survivors.Single(s => s.Kind == RedactionSurvivorKind.Annotation).Text.Should().Be("NOTE");
    }

    [Fact]
    public void Verify_IgnoresInvisibleText_AndTextBesideTheBox()
    {
        // Render mode 3 is invisible; the second run sits left of the box.
        using PdfDocument doc = Open(TextPage(
            "BT 3 Tr /F1 12 Tf 300 300 Td (HIDDEN) Tj ET BT /F1 12 Tf 200 300 Td (AB) Tj ET"));

        RedactionVerifier.Verify(doc, new[] { new RedactionRect(0, Box) }).IsClean.Should().BeTrue();
    }

    [Fact]
    public void Verify_FindsTextInsideAForm()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</XObject<</Fm0 6 0 R>>>>/Contents 4 0 R>>"),
            Stream("q 1 0 0 1 280 280 cm /Fm0 Do Q"),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 100 100]/Resources<</Font<</F1 5 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (FORMTXT) Tj ET"),
        };
        using PdfDocument doc = Open(Build(bodies));

        RedactionVerifier.Verify(doc, new[] { new RedactionRect(0, Box) }).SurvivingText.Should().Be("FORMTXT");
    }

    [Fact]
    public void Verify_SeesTextInAHiddenLayer()
    {
        // Hidden layers are not drawn by default, but their text is still in
        // the file, so verification must report it.
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R/OCProperties<</OCGs[6 0 R]/D<</OFF[6 0 R]>>>>>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>"
                + "/Properties<</L 6 0 R>>>>/Contents 4 0 R>>"),
            Stream("/OC /L BDC BT /F1 12 Tf 300 300 Td (LAYERED) Tj ET EMC"),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
            Obj("<</Type/OCG/Name(Secret)>>"),
        };
        using PdfDocument doc = Open(Build(bodies));

        RedactionVerifier.Verify(doc, new[] { new RedactionRect(0, Box) }).SurvivingText.Should().Be("LAYERED");
    }

    [Fact]
    public void ExtractWithin_RejectsBadArguments()
    {
        using PdfDocument doc = Open(TextPage("BT ET"));

        Action badPage = () => RedactionVerifier.ExtractWithin(doc, 3, Box);
        Action nullDoc = () => RedactionVerifier.ExtractWithin(null!, 0, Box);
        Action nullRects = () => RedactionVerifier.Verify(doc, null!);

        badPage.Should().Throw<ArgumentOutOfRangeException>();
        nullDoc.Should().Throw<ArgumentNullException>();
        nullRects.Should().Throw<ArgumentNullException>();
    }

    // ── helpers ──

    private static bool Leaks(string pdf, string secret)
    {
        string hex = Convert.ToHexString(Encoding.Latin1.GetBytes(secret));
        return pdf.Contains(secret, StringComparison.Ordinal)
            || pdf.Contains(hex, StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactTagged(string content, string extraCatalog = "", params byte[][] extra)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R/MarkInfo<</Marked true>>" + extraCatalog + ">>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj(Page),
            Stream(content),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
        };
        bodies.AddRange(extra);
        return Redact(bodies);
    }

    private static string Redact(List<byte[]> bodies)
    {
        byte[] output = RedactBytes(Build(bodies), new[] { new RedactionRect(0, Box) });
        return Encoding.Latin1.GetString(output);
    }

    private static byte[] RedactBytes(byte[] source, IReadOnlyList<RedactionRect> rects)
    {
        using PdfDocument doc = Open(source);
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions { Rectangles = rects.ToList() });
        return output.ToArray();
    }

    private static byte[] TextPage(string content)
    {
        return Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(content),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
        });
    }

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string content) => Stream(string.Empty, content);

    private static byte[] Stream(string entries, string content)
    {
        byte[] data = Encoding.Latin1.GetBytes(content);
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<<" + entries + "/Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(data);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Build(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            ms.Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
        return ms.ToArray();
    }
}
