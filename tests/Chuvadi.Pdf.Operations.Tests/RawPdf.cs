// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Test helper: assembles small hand-written PDFs object by object.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chuvadi.Pdf.Operations.Tests;

/// <summary>
/// Builds minimal PDFs from literal object bodies so tests can exercise
/// constructs the authoring API never emits (crop boxes, /Rotate, inline
/// images, patterns, marked content). Object N is the Nth body (1-based); the
/// catalog must be object 1.
/// </summary>
internal static class RawPdf
{
    /// <summary>Returns a stream object body for the given dictionary entries and content.</summary>
    internal static byte[] Stream(string dictEntries, string content)
    {
        return Stream(dictEntries, Encoding.Latin1.GetBytes(content));
    }

    /// <summary>Returns a stream object body for the given dictionary entries and raw bytes.</summary>
    internal static byte[] Stream(string dictEntries, byte[] content)
    {
        using MemoryStream ms = new MemoryStream();
        byte[] head = Encoding.Latin1.GetBytes(
            "<<" + dictEntries + "/Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n");
        ms.Write(head);
        ms.Write(content);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    /// <summary>Returns a non-stream object body.</summary>
    internal static byte[] Obj(string body)
    {
        return Encoding.Latin1.GetBytes(body);
    }

    /// <summary>Assembles the bodies into a complete PDF with a classic xref table.</summary>
    internal static byte[] Build(IReadOnlyList<byte[]> bodies)
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

    /// <summary>
    /// A one-page PDF: catalog, pages, the page (with the given extra page
    /// dictionary entries) and its content stream as object 4.
    /// </summary>
    internal static byte[] SinglePage(string pageEntries, string content, params byte[][] extraObjects)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + pageEntries + "/Contents 4 0 R>>"),
            Stream(string.Empty, content),
        };
        bodies.AddRange(extraObjects);
        return Build(bodies);
    }
}
