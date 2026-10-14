// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.19: an image whose Do is removed by redaction leaves the file,
// not just the content stream (shared images used elsewhere are kept).

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionImageRemovalTests
{
    private const string Media = "/MediaBox[0 0 400 400]";

    [Fact]
    public void ImageUnderTheBox_IsRemovedFromTheFile()
    {
        string output = Redact(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 50 0 0 50 300 300 cm /Im0 Do Q"),
            MarkerImage(),
        });

        output.Should().NotContain("QZX", "the removed image's bytes must not remain in the file");
        output.Should().NotContain("/Im0 Do");
    }

    [Fact]
    public void ImageInsideAFormUnderTheBox_IsRemovedFromTheFile()
    {
        string output = Redact(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Fm0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "/Fm0 Do"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</XObject<</Im0 6 0 R>>>>",
                "q 50 0 0 50 300 300 cm /Im0 Do Q"),
            MarkerImage(),
        });

        output.Should().NotContain("QZX");
    }

    [Fact]
    public void ImageSharedWithAnUnredactedPage_IsKept()
    {
        string output = Redact(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R 5 0 R]/Count 2>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 7 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 50 0 0 50 300 300 cm /Im0 Do Q"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 7 0 R>>>>/Contents 6 0 R>>"),
            Stream(string.Empty, "q 50 0 0 50 300 300 cm /Im0 Do Q"),
            MarkerImage(),
        });

        output.Should().Contain("QZX", "page 2 still paints the shared image");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(output)), leaveOpen: false);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public void ImageOutsideTheBox_IsKept()
    {
        string output = Redact(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 50 0 0 50 20 20 cm /Im0 Do Q"),
            MarkerImage(),
        });

        output.Should().Contain("QZX");
        output.Should().Contain("/Im0 Do");
    }

    [Fact]
    public void ImageDrawnByAResourceInheritingForm_IsKept()
    {
        // The form has no /Resources: it draws /Im1 from the page's resources.
        // Pruning must count that use, or the form would lose its image.
        string output = Redact(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media
                + "/Resources<</XObject<</Fm0 5 0 R/Im1 6 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "BT ET /Fm0 Do"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]", "q 50 0 0 50 20 20 cm /Im1 Do Q"),
            MarkerImage(),
        });

        output.Should().Contain("QZX", "the inheriting form still uses the image");
    }

    private static string Redact(List<byte[]> bodies)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Build(bodies)), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(250, 250, 150, 100)),
            },
        });

        return Encoding.Latin1.GetString(output.ToArray());
    }

    // A 4x4 RGB image whose every pixel is the marker bytes "QZX".
    private static byte[] MarkerImage()
    {
        byte[] data = new byte[4 * 4 * 3];
        for (int i = 0; i < data.Length; i += 3)
        {
            data[i] = (byte)'Q';
            data[i + 1] = (byte)'Z';
            data[i + 2] = (byte)'X';
        }

        return Stream("/Type/XObject/Subtype/Image/Width 4/Height 4/ColorSpace/DeviceRGB/BitsPerComponent 8", data);
    }

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string entries, string content) => Stream(entries, Encoding.Latin1.GetBytes(content));

    private static byte[] Stream(string entries, byte[] content)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<<" + entries + "/Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(content);
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
