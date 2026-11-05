// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — builds small PDFs for the 3.31 fixtures (E1 to E6, C1 to C15,
// Print items, O9). Every fixture is made here; no PDF files are committed.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Pdf.Integration.Tests;

/// <summary>A one-page (or more) PDF assembled from object bodies.</summary>
internal sealed class TestPdf
{
    private readonly List<(string Body, byte[]? Stream)> _objects = new List<(string, byte[]?)>();

    /// <summary>Adds an object and returns its number.</summary>
    internal int Add(string body)
    {
        _objects.Add((body, null));
        return _objects.Count;
    }

    /// <summary>Adds a stream object (the dictionary without /Length) and returns its number.</summary>
    internal int AddStream(string dict, byte[] data)
    {
        _objects.Add((dict, data));
        return _objects.Count;
    }

    internal int AddStream(string dict, string data) => AddStream(dict, Encoding.Latin1.GetBytes(data));

    /// <summary>Reserves an object number to fill later with <see cref="Set"/>.</summary>
    internal int Reserve()
    {
        _objects.Add((string.Empty, null));
        return _objects.Count;
    }

    internal void Set(int number, string body) => _objects[number - 1] = (body, null);

    internal void SetStream(int number, string dict, byte[] data) => _objects[number - 1] = (dict, data);

    /// <summary>Writes the file with <paramref name="catalog"/> as its root.</summary>
    internal byte[] Build(int catalog)
    {
        using MemoryStream ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.7\n%âãÏÓ\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < _objects.Count; i++)
        {
            offsets.Add(ms.Position);
            (string body, byte[]? stream) = _objects[i];
            W((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            if (stream is null)
            {
                W(body + "\nendobj\n");
            }
            else
            {
                string dict = body.TrimEnd();
                dict = dict[..^2] + "/Length " + stream.Length.ToString(CultureInfo.InvariantCulture) + ">>";
                W(dict + "\nstream\n");
                ms.Write(stream);
                W("\nendstream\nendobj\n");
            }
        }

        long xref = ms.Position;
        W("xref\n0 " + (_objects.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            W(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        W("trailer\n<</Size " + (_objects.Count + 1).ToString(CultureInfo.InvariantCulture) + "/Root " + catalog.ToString(CultureInfo.InvariantCulture) + " 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>
    /// A one-page document: a 200 x 200 page with <paramref name="content"/>,
    /// <paramref name="resources"/>, annotations and an optional AcroForm.
    /// The annotation bodies may refer to "PAGE" (replaced by the page reference).
    /// </summary>
    internal static byte[] OnePage(string content, string resources, IReadOnlyList<string> annotations, string? acroForm = null, string extraPage = "")
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", content);
        List<int> annots = new List<int>();
        foreach (string a in annotations)
        {
            annots.Add(pdf.Add(a.Replace("PAGE", page.ToString(CultureInfo.InvariantCulture) + " 0 R", System.StringComparison.Ordinal)));
        }

        StringBuilder annotRefs = new StringBuilder();
        foreach (int n in annots)
        {
            annotRefs.Append(n.ToString(CultureInfo.InvariantCulture)).Append(" 0 R ");
        }

        string form = acroForm is null ? string.Empty : "/AcroForm " + acroForm.Replace("FIELDS", annotRefs.ToString(), System.StringComparison.Ordinal);
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R" + form + ">>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + page.ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        pdf.Set(page, "<</Type/Page/Parent " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources
            + "/Contents " + contents.ToString(CultureInfo.InvariantCulture) + " 0 R" + (annots.Count > 0 ? "/Annots[" + annotRefs + "]" : string.Empty) + extraPage + ">>");
        return pdf.Build(catalog);
    }

    internal static PdfDocument Open(byte[] bytes, List<Chuvadi.Pdf.Primitives.PdfWarning>? warnings = null) =>
        PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { OnWarning = w => warnings?.Add(w) });

    /// <summary>Dark pixels inside a PDF-space rectangle of page 1, rendered at 72 dpi.</summary>
    internal static int Ink(PdfDocument doc, Chuvadi.Pdf.Documents.AnnotationMode mode, double x0, double y0, double x1, double y1)
    {
        PixelBuffer buf = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AnnotationMode = mode }).Rasterize(doc.Pages[0]);
        int count = 0;
        for (int y = (int)(200 - y1); y < (int)(200 - y0); y++)
        {
            for (int x = (int)x0; x < (int)x1; x++)
            {
                (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(x, y);
                if ((b + g + r) / 3 < 200)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
