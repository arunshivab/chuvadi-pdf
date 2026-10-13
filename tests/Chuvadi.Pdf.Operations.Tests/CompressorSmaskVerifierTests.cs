// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// VERIFIER: adversarial test for 3.18 SmartCodecSelection — a stream that is
// the /SMask of another image must stay DeviceGray (§11.6.5.2), but the
// palette recoder has no way to know a grayscale image is a soft mask.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class CompressorSmaskVerifierTests
{
    [Fact]
    public void SmartCodec_SoftMaskStream_MustStayDeviceGray()
    {
        byte[] pdf = BuildPdfWithSoftMaskedImage();
        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PdfCompressor.Compress(document, output, new CompressionOptions
        {
            RecompressImages = true,
            SmartCodecSelection = true,
            MinImagePixelsToRecompress = 1,
        });

        using PdfDocument result = PdfDocument.Open(
            new MemoryStream(output.ToArray()), leaveOpen: true);

        // Find the base image and follow its /SMask.
        PdfDictionary? resources = result.Pages[0].Resources;
        Assert.NotNull(resources);
        resources!.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv);
        PdfDictionary xobjects = (PdfDictionary)result.Objects.Resolve(xv!);

        PdfStream? mask = null;
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xobjects)
        {
            if (result.Objects.Resolve(entry.Value) is PdfStream stream
                && stream.Dictionary.TryGetValue(
                    PdfName.Intern("SMask"), out PdfPrimitive? maskRef))
            {
                mask = result.Objects.Resolve(maskRef) as PdfStream;
            }
        }

        Assert.NotNull(mask);
        PdfPrimitive? cs = mask!.Dictionary.TryGetValue(
            PdfName.Intern("ColorSpace"), out PdfPrimitive? csPrim)
            ? result.Objects.Resolve(csPrim)
            : null;

        bool isDeviceGray = cs is PdfName n && n.Value == "DeviceGray";
        Assert.True(isDeviceGray,
            $"the soft-mask stream's ColorSpace became '{Describe(cs)}' — a "
            + "soft-mask image shall be DeviceGray (PDF 32000-1 §11.6.5.2)");
    }

    private static string Describe(PdfPrimitive? p)
    {
        return p switch
        {
            null => "(none)",
            PdfName n => "/" + n.Value,
            PdfArray a => "array[" + a.Count + "] "
                + (a.Count > 0 && a[0] is PdfName f ? "/" + f.Value : ""),
            _ => p.GetType().Name,
        };
    }

    private static byte[] BuildPdfWithSoftMaskedImage()
    {
        // Base image: 8x8 RGB. Mask: 100x100 DeviceGray, constant value —
        // trivially palettable.
        byte[] baseData = new byte[8 * 8 * 3];
        for (int i = 0; i < baseData.Length; i++)
        {
            baseData[i] = (byte)(i * 7);
        }

        byte[] maskData = new byte[100 * 100];
        for (int i = 0; i < maskData.Length; i++)
        {
            maskData[i] = 128;
        }

        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
                + "/Resources<</XObject<</Im1 5 0 R>>>>/Contents 4 0 R>>"),
            StreamObj("<</Length 33>>", Encoding.Latin1.GetBytes(
                "q 100 0 0 100 50 50 cm /Im1 Do Q\n")),
            StreamObj(
                "<</Type/XObject/Subtype/Image/Width 8/Height 8"
                + "/ColorSpace/DeviceRGB/BitsPerComponent 8/SMask 6 0 R"
                + "/Length " + baseData.Length + ">>", baseData),
            StreamObj(
                "<</Type/XObject/Subtype/Image/Width 100/Height 100"
                + "/ColorSpace/DeviceGray/BitsPerComponent 8"
                + "/Length " + maskData.Length + ">>", maskData),
        };

        return Assemble(bodies);
    }

    private static byte[] StreamObj(string dict, byte[] data)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(dict + "\nstream\n"));
        ms.Write(data);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Assemble(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj ");
            ms.Write(bodies[i]);
            Write(" endobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)
                + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1)
            + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }
}
