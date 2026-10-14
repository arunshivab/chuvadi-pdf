// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.20: downsampling measures images painted inside form XObjects
// (composing /Matrix and the invoking CTM, with resource inheritance), and
// still leaves alone any image with a use it cannot measure.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class NestedFormDownsampleTests
{
    private const string ImageDict =
        "/Type/XObject/Subtype/Image/Width 400/Height 400/ColorSpace/DeviceRGB/BitsPerComponent 8";

    [Fact]
    public void ImageInsideAScaledForm_IsMeasuredAndDownsampled()
    {
        // Page scales by 2, the form's /Matrix by 0.5, the form places the
        // image at 72 pt: 400 px over one inch = 400 DPI → 100 px at 100 DPI.
        byte[] source = RawPdf.SinglePage(
            "/Resources<</XObject<</Fm0 5 0 R>>>>",
            "q 2 0 0 2 0 0 cm /Fm0 Do Q",
            RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Matrix[0.5 0 0 0.5 0 0]"
                + "/Resources<</XObject<</Im0 6 0 R>>>>", "q 72 0 0 72 50 50 cm /Im0 Do Q"),
            RawPdf.Stream(ImageDict, Noise()));

        ImageWidth(Compress(source)).Should().Be(100);
    }

    [Fact]
    public void ImageDrawnByAResourceInheritingForm_IsMeasured()
    {
        byte[] source = RawPdf.SinglePage(
            "/Resources<</XObject<</Fm0 5 0 R/Im0 6 0 R>>>>",
            "/Fm0 Do",
            RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]", "q 72 0 0 72 50 50 cm /Im0 Do Q"),
            RawPdf.Stream(ImageDict, Noise()));

        ImageWidth(Compress(source)).Should().Be(100);
    }

    [Fact]
    public void LargestPlacementAcrossFormAndPage_Wins()
    {
        // Small on the page (72 pt), large inside the form (288 pt): the
        // form use decides, so 400 px over four inches stays below 100 DPI.
        byte[] source = RawPdf.SinglePage(
            "/Resources<</XObject<</Fm0 5 0 R/Im0 6 0 R>>>>",
            "q 72 0 0 72 0 0 cm /Im0 Do Q /Fm0 Do",
            RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]", "q 288 0 0 288 50 50 cm /Im0 Do Q"),
            RawPdf.Stream(ImageDict, Noise()));

        ImageWidth(Compress(source)).Should().Be(400);
    }

    [Fact]
    public void ImageAlsoUsedInAPattern_IsNotDownsampled()
    {
        byte[] source = RawPdf.SinglePage(
            "/Resources<</XObject<</Im0 6 0 R>>/Pattern<</P0 5 0 R>>>>",
            "q 72 0 0 72 50 50 cm /Im0 Do Q",
            RawPdf.Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 10 10]/XStep 10/YStep 10"
                + "/Resources<</XObject<</Im0 6 0 R>>>>", "q 10 0 0 10 0 0 cm /Im0 Do Q"),
            RawPdf.Stream(ImageDict, Noise()));

        ImageWidth(Compress(source)).Should().Be(400, "the pattern use is not measured");
    }

    [Fact]
    public void ImageAlsoUsedInAnAnnotationAppearance_IsNotDownsampled()
    {
        byte[] source = RawPdf.SinglePage(
            "/Resources<</XObject<</Im0 6 0 R>>>>/Annots[7 0 R]",
            "q 72 0 0 72 50 50 cm /Im0 Do Q",
            RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</XObject<</Im0 6 0 R>>>>",
                "q 400 0 0 400 0 0 cm /Im0 Do Q"),
            RawPdf.Stream(ImageDict, Noise()),
            RawPdf.Obj("<</Type/Annot/Subtype/Stamp/Rect[0 0 400 400]/AP<</N 5 0 R>>>>"));

        ImageWidth(Compress(source)).Should().Be(400, "the appearance use is not measured");
    }

    [Fact]
    public void RecursiveForm_DoesNotHang_AndIsNotDownsampled()
    {
        byte[] source = RawPdf.SinglePage(
            "/Resources<</XObject<</Fm0 5 0 R>>>>",
            "/Fm0 Do",
            RawPdf.Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</XObject<</Fm0 5 0 R/Im0 6 0 R>>>>",
                "q 72 0 0 72 50 50 cm /Im0 Do Q /Fm0 Do"),
            RawPdf.Stream(ImageDict, Noise()));

        ImageWidth(Compress(source)).Should().Be(400);
    }

    private static byte[] Compress(byte[] source)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PdfCompressor.Compress(document, output, new CompressionOptions
        {
            RecompressImages = true,
            DownsampleImagesToDpi = 100,
        });
        return output.ToArray();
    }

    // Finds the image by walking page → XObjects (→ form XObjects) and
    // returns its /Width.
    private static int ImageWidth(byte[] pdf)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        PdfStream? image = FindImage(document.Pages[0].Resources, document, 0);
        image.Should().NotBeNull();
        return ((PdfInteger)document.Objects.Resolve(image!.Dictionary.GetAs<PdfPrimitive>(PdfName.Intern("Width"))!)).Value;
    }

    private static PdfStream? FindImage(PdfDictionary? resources, PdfDocument document, int depth)
    {
        if (resources is null || depth > 4
            || !resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv)
            || document.Objects.Resolve(xv) is not PdfDictionary xobjects)
        {
            return null;
        }

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xobjects)
        {
            if (document.Objects.Resolve(entry.Value) is not PdfStream stream
                || stream.Dictionary.Subtype is not PdfName subtype)
            {
                continue;
            }

            if (subtype.Value == "Image")
            {
                return stream;
            }

            if (subtype.Value == "Form"
                && stream.Dictionary.TryGetValue(PdfName.Resources, out PdfPrimitive? rv)
                && FindImage(document.Objects.Resolve(rv) as PdfDictionary, document, depth + 1) is PdfStream found)
            {
                return found;
            }
        }

        return null;
    }

    private static byte[] Noise()
    {
        byte[] data = new byte[400 * 400 * 3];
        uint seed = 42;
        for (int i = 0; i < data.Length; i++)
        {
            seed = (seed * 1664525u) + 1013904223u;
            data[i] = (byte)(seed >> 16);
        }

        return data;
    }
}
