// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1:2008 Annex F — Linearized PDF
// PHASE: Phase 1.1.6 — Chuvadi.Pdf.IO linearization
//
// Builds a linearized PDF using a deterministic three-phase strategy:
//
//   Phase A: serialise every existing object to its own byte[]. No streaming;
//            each object's bytes are independent of file position.
//   Phase B: decide layout order (first-page section, catalog, hint stream,
//            remaining-pages section, leftover objects, main xref).
//   Phase C: compute byte offsets by summing serialised lengths in layout order.
//            All offsets including those embedded inside the param dict and
//            the hint stream are now known exactly.
//   Phase D: assemble the final byte array — header, then each laid-out piece
//            in order. The param dict and hint stream are built fresh with the
//            correct offsets baked in.
//
// Special case for /L (total file length): /L itself appears inside the param
// dict, so the param dict's serialised length depends on the magnitude of /L.
// Solved by a small fixed-point loop (at most two iterations) that stabilises.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.IO;

internal static class LinearizedWriter
{
    // 3.33.0 (D-318): written as Latin-1, so the binary comment's four bytes
    // are above 127; before, ASCII turned them into "????".
    private const string PdfHeader = "%PDF-1.7\n%\xE2\xE3\xCF\xD3\n";

    public static void Write(
        Stream output,
        IList<PdfIndirectObject> objects,
        PdfDictionary trailer)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(trailer);

        // ── Categorise: which objects go in the first-page section? ──────
        Categorization cat = Categorize(objects, trailer);

        // Allocate object numbers for the linearization param dict and the
        // primary hint stream object. These are new indirect objects.
        int maxObjNum = 0;
        foreach (PdfIndirectObject o in objects)
        {
            if (o.Id.ObjectNumber > maxObjNum) { maxObjNum = o.Id.ObjectNumber; }
        }
        int paramDictObjNum = maxObjNum + 1;
        int hintStreamObjNum = maxObjNum + 2;

        // ── Phase A: serialise every existing object to its own bytes ────
        Dictionary<int, byte[]> objectBytes = new();
        foreach (PdfIndirectObject obj in objects)
        {
            objectBytes[obj.Id.ObjectNumber] = SerialiseObject(obj);
        }

        // ── Phase B: lay out in linearization order ──────────────────────
        // Order:
        //   1. param dict (object N+1)
        //   2. first-page xref
        //   3. catalog
        //   4. pages-root
        //   5. first-page section objects
        //   6. hint stream (object N+2)
        //   7. remaining pages (page 2..N) in order, plus their reachable objects
        //   8. everything else
        //   9. main xref + trailer

        List<int> firstPageSection = new();
        foreach (int n in cat.FirstPageReachable)
        {
            if (n == cat.RootCatalog) { continue; }
            if (n == cat.PagesRoot) { continue; }
            firstPageSection.Add(n);
        }

        List<int> remainingSection = new();
        HashSet<int> placed = new() { cat.RootCatalog };
        if (cat.PagesRoot > 0) { placed.Add(cat.PagesRoot); }
        foreach (int n in firstPageSection) { placed.Add(n); }

        // Add remaining pages in document order, then anything else.
        for (int pIdx = 1; pIdx < cat.PageOrder.Count; pIdx++)
        {
            int pageObj = cat.PageOrder[pIdx];
            if (placed.Add(pageObj))
            {
                remainingSection.Add(pageObj);
            }
        }

        foreach (PdfIndirectObject o in objects)
        {
            if (placed.Add(o.Id.ObjectNumber))
            {
                remainingSection.Add(o.Id.ObjectNumber);
            }
        }

        // ── Phase C: compute exact offsets with fixed-point on /L ────────
        long L = 0;
        long paramDictOffset = 0;
        long firstPageXrefOffset = 0;
        long firstPageXrefLength = 0;
        long catalogOffset = 0;
        long pagesRootOffset = 0;
        long firstPageObjectOffset = 0;
        long endOfFirstPage = 0;
        long hintStreamOffset = 0;
        long hintStreamLength = 0;
        long mainXrefOffset = 0;

        byte[] paramDictBytes = Array.Empty<byte>();
        byte[] hintStreamBytes = Array.Empty<byte>();
        byte[] firstPageXrefBytes = Array.Empty<byte>();
        byte[] mainXrefAndTrailerBytes = Array.Empty<byte>();

        // The /L value influences param dict serialised length. Iterate to fixed point.
        for (int iter = 0; iter < 4; iter++)
        {
            // Build param dict with current estimate of L (and hint offset/length).
            PdfDictionary paramDict = new();
            paramDict.Set(PdfName.Intern("Linearized"), new PdfReal(1.0));
            paramDict.Set(PdfName.Intern("L"), (int)L);
            paramDict.Set(PdfName.Intern("H"), new PdfArray([
                new PdfInteger((int)hintStreamOffset),
                new PdfInteger((int)hintStreamLength),
            ]));
            paramDict.Set(PdfName.Intern("O"), cat.PageOrder.Count > 0 ? cat.PageOrder[0] : 0);
            paramDict.Set(PdfName.Intern("E"), (int)endOfFirstPage);
            paramDict.Set(PdfName.Intern("N"), cat.PageOrder.Count);
            paramDict.Set(PdfName.Intern("T"), (int)mainXrefOffset);
            paramDictBytes = SerialiseObject(new PdfIndirectObject(
                new PdfObjectId(paramDictObjNum, 0), paramDict));

            // Compute layout offsets.
            long pos = PdfHeader.Length;
            paramDictOffset = pos;
            pos += paramDictBytes.Length;

            // First-page xref placeholder offset; we'll compute its length below.
            firstPageXrefOffset = pos;

            // Build first-page xref content (we need offsets first, so do this
            // in two sub-steps).
            XrefTable firstPageXref = new();
            // Param dict object goes in the first-page xref (must be reachable
            // from a viewer that's only fetched the first-page portion).
            firstPageXref.Set(new XrefEntry(paramDictObjNum, 0, paramDictOffset));

            // Reserve first-page xref space: we need its size. Compute by
            // building it once with placeholder offsets, then taking its length.
            // Catalog + pages-root + first-page objects + param dict + hint stream.
            // For length-computation only:
            XrefTable sizingXref = new();
            sizingXref.Set(new XrefEntry(paramDictObjNum, 0, 0));
            sizingXref.Set(new XrefEntry(cat.RootCatalog, 0, 0));
            if (cat.PagesRoot > 0) { sizingXref.Set(new XrefEntry(cat.PagesRoot, 0, 0)); }
            foreach (int n in firstPageSection) { sizingXref.Set(new XrefEntry(n, 0, 0)); }
            sizingXref.Set(new XrefEntry(hintStreamObjNum, 0, 0));
            using MemoryStream sizingMs = new();
            sizingXref.Write(sizingMs);
            firstPageXrefLength = sizingMs.Length;
            pos += firstPageXrefLength;

            // Catalog
            catalogOffset = pos;
            pos += objectBytes[cat.RootCatalog].Length;

            // Pages root
            if (cat.PagesRoot > 0)
            {
                pagesRootOffset = pos;
                pos += objectBytes[cat.PagesRoot].Length;
            }

            // First-page section
            foreach (int n in firstPageSection)
            {
                if (n == cat.PageOrder[0])
                {
                    firstPageObjectOffset = pos;
                }
                pos += objectBytes[n].Length;
            }

            endOfFirstPage = pos;

            // Hint stream — build with current offsets, then place
            hintStreamOffset = pos;
            hintStreamBytes = BuildHintStreamObject(
                hintStreamObjNum, cat.PageOrder, objectBytes, firstPageSection);
            hintStreamLength = hintStreamBytes.Length;
            pos += hintStreamBytes.Length;

            // Remaining section
            foreach (int n in remainingSection)
            {
                pos += objectBytes[n].Length;
            }

            // Main xref
            mainXrefOffset = pos;

            // Build the main xref (contains every object).
            XrefTable mainXref = new();
            mainXref.Set(new XrefEntry(paramDictObjNum, 0, paramDictOffset));
            mainXref.Set(new XrefEntry(cat.RootCatalog, 0, catalogOffset));
            if (cat.PagesRoot > 0) { mainXref.Set(new XrefEntry(cat.PagesRoot, 0, pagesRootOffset)); }

            long walkPos = catalogOffset + objectBytes[cat.RootCatalog].Length;
            if (cat.PagesRoot > 0) { walkPos += objectBytes[cat.PagesRoot].Length; }

            foreach (int n in firstPageSection)
            {
                mainXref.Set(new XrefEntry(n, 0, walkPos));
                walkPos += objectBytes[n].Length;
            }

            mainXref.Set(new XrefEntry(hintStreamObjNum, 0, hintStreamOffset));
            walkPos = hintStreamOffset + hintStreamBytes.Length;

            foreach (int n in remainingSection)
            {
                mainXref.Set(new XrefEntry(n, 0, walkPos));
                walkPos += objectBytes[n].Length;
            }

            // Serialise main xref + trailer
            using MemoryStream mxMs = new();
            mainXref.Write(mxMs);

            PdfDictionary trailerCopy = CloneDict(trailer);
            trailerCopy.Set(PdfName.Size, FindHighestObjectNumber(
                paramDictObjNum, hintStreamObjNum, objects) + 1);

            byte[] trailerLineBytes = Encoding.ASCII.GetBytes("trailer\n");
            mxMs.Write(trailerLineBytes, 0, trailerLineBytes.Length);
            PdfWriter.WriteValue(mxMs, trailerCopy);
            mxMs.WriteByte((byte)'\n');

            byte[] startxref = Encoding.ASCII.GetBytes(
                $"startxref\n{mainXrefOffset}\n%%EOF\n");
            mxMs.Write(startxref, 0, startxref.Length);

            mainXrefAndTrailerBytes = mxMs.ToArray();

            // Total file length
            long newL = pos + mainXrefAndTrailerBytes.Length;

            // Rebuild the first-page xref with real offsets
            using MemoryStream fxMs = new();
            firstPageXref = new XrefTable();
            firstPageXref.Set(new XrefEntry(paramDictObjNum, 0, paramDictOffset));
            firstPageXref.Set(new XrefEntry(cat.RootCatalog, 0, catalogOffset));
            if (cat.PagesRoot > 0)
            {
                firstPageXref.Set(new XrefEntry(cat.PagesRoot, 0, pagesRootOffset));
            }
            walkPos = catalogOffset + objectBytes[cat.RootCatalog].Length;
            if (cat.PagesRoot > 0) { walkPos += objectBytes[cat.PagesRoot].Length; }
            foreach (int n in firstPageSection)
            {
                firstPageXref.Set(new XrefEntry(n, 0, walkPos));
                walkPos += objectBytes[n].Length;
            }
            firstPageXref.Set(new XrefEntry(hintStreamObjNum, 0, hintStreamOffset));
            firstPageXref.Write(fxMs);

            // Pad the first-page xref to exactly firstPageXrefLength so our
            // size estimate matches reality.
            byte[] rawFxBytes = fxMs.ToArray();
            firstPageXrefBytes = new byte[firstPageXrefLength];
            if (rawFxBytes.Length > firstPageXrefLength)
            {
                throw new InvalidOperationException(
                    $"First-page xref grew unexpectedly: {rawFxBytes.Length} > {firstPageXrefLength}");
            }
            Buffer.BlockCopy(rawFxBytes, 0, firstPageXrefBytes, 0, rawFxBytes.Length);
            // Pad with spaces, ending in newline so the file is still parseable.
            for (int i = rawFxBytes.Length; i < firstPageXrefBytes.Length - 1; i++)
            {
                firstPageXrefBytes[i] = (byte)' ';
            }
            if (firstPageXrefBytes.Length > 0)
            {
                firstPageXrefBytes[firstPageXrefBytes.Length - 1] = (byte)'\n';
            }

            if (newL == L && iter > 0)
            {
                break;
            }
            L = newL;
        }

        // ── Phase D: assemble final output ────────────────────────────────
        output.Write(Encoding.Latin1.GetBytes(PdfHeader), 0, PdfHeader.Length);
        output.Write(paramDictBytes, 0, paramDictBytes.Length);
        output.Write(firstPageXrefBytes, 0, firstPageXrefBytes.Length);
        output.Write(objectBytes[cat.RootCatalog], 0, objectBytes[cat.RootCatalog].Length);
        if (cat.PagesRoot > 0)
        {
            output.Write(objectBytes[cat.PagesRoot], 0, objectBytes[cat.PagesRoot].Length);
        }
        foreach (int n in firstPageSection)
        {
            output.Write(objectBytes[n], 0, objectBytes[n].Length);
        }
        output.Write(hintStreamBytes, 0, hintStreamBytes.Length);
        foreach (int n in remainingSection)
        {
            output.Write(objectBytes[n], 0, objectBytes[n].Length);
        }
        output.Write(mainXrefAndTrailerBytes, 0, mainXrefAndTrailerBytes.Length);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private sealed class Categorization
    {
        public int RootCatalog;
        public int PagesRoot;
        public List<int> PageOrder = new();
        public HashSet<int> FirstPageReachable = new();
    }

    private static Categorization Categorize(
        IList<PdfIndirectObject> objects, PdfDictionary trailer)
    {
        Categorization cat = new();
        Dictionary<int, PdfIndirectObject> byId = new();

        foreach (PdfIndirectObject o in objects)
        {
            byId[o.Id.ObjectNumber] = o;
        }

        if (trailer.TryGetValue(PdfName.Intern("Root"), out PdfPrimitive? rootP) &&
            rootP is PdfReference rootR)
        {
            cat.RootCatalog = rootR.ObjectId.ObjectNumber;
        }

        if (cat.RootCatalog > 0 &&
            byId.TryGetValue(cat.RootCatalog, out PdfIndirectObject? catObj) &&
            catObj.Value is PdfDictionary catDict &&
            catDict.TryGetValue(PdfName.Intern("Pages"), out PdfPrimitive? pgP) &&
            pgP is PdfReference pgR)
        {
            cat.PagesRoot = pgR.ObjectId.ObjectNumber;
        }

        if (cat.PagesRoot > 0)
        {
            WalkPages(cat.PagesRoot, byId, cat.PageOrder);
        }

        if (cat.PageOrder.Count > 0)
        {
            CollectReachable(cat.PageOrder[0], byId, cat.FirstPageReachable);
        }

        return cat;
    }

    private static void WalkPages(
        int nodeObjNum,
        Dictionary<int, PdfIndirectObject> byId,
        List<int> result)
    {
        if (!byId.TryGetValue(nodeObjNum, out PdfIndirectObject? node) ||
            node.Value is not PdfDictionary dict)
        {
            return;
        }

        if (dict.TryGetValue(PdfName.Type, out PdfPrimitive? typeP) &&
            typeP is PdfName typeName && typeName.Value == "Page")
        {
            result.Add(nodeObjNum);
            return;
        }

        if (dict.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsP) &&
            kidsP is PdfArray kids)
        {
            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i] is PdfReference r)
                {
                    WalkPages(r.ObjectId.ObjectNumber, byId, result);
                }
            }
        }
    }

    private static void CollectReachable(
        int rootObjNum,
        Dictionary<int, PdfIndirectObject> byId,
        HashSet<int> visited)
    {
        Stack<int> stack = new();
        stack.Push(rootObjNum);

        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (!visited.Add(n)) { continue; }
            if (!byId.TryGetValue(n, out PdfIndirectObject? obj)) { continue; }
            CollectRefs(obj.Value, stack);
        }
    }

    private static void CollectRefs(PdfPrimitive value, Stack<int> stack)
    {
        switch (value)
        {
            case PdfReference r:
                stack.Push(r.ObjectId.ObjectNumber);
                break;
            case PdfArray arr:
                for (int i = 0; i < arr.Count; i++) { CollectRefs(arr[i], stack); }
                break;
            case PdfDictionary dict:
                foreach (KeyValuePair<PdfName, PdfPrimitive> kvp in dict)
                {
                    if (kvp.Key.Value == "Parent") { continue; }
                    CollectRefs(kvp.Value, stack);
                }
                break;
            case PdfStream stream:
                CollectRefs(stream.Dictionary, stack);
                break;
        }
    }

    private static byte[] SerialiseObject(PdfIndirectObject obj)
    {
        using MemoryStream ms = new();
        PdfWriter.WriteIndirectObject(ms, obj);
        return ms.ToArray();
    }

    private static byte[] BuildHintStreamObject(
        int objNum,
        List<int> pageOrder,
        Dictionary<int, byte[]> objectBytes,
        List<int> firstPageSection)
    {
        List<PageHintEntry> hints = new(pageOrder.Count);
        for (int i = 0; i < pageOrder.Count; i++)
        {
            int objSize = objectBytes.TryGetValue(pageOrder[i], out byte[]? b) ? b.Length : 0;
            hints.Add(new PageHintEntry
            {
                ObjectCount = (i == 0) ? firstPageSection.Count : 1,
                LengthInBytes = objSize,
                ContentStreamOffset = 0,
                ContentStreamLength = 0,
            });
        }

        byte[] hintBody = PageHintTable.Encode(hints,
            pageOrder.Count > 0 ? pageOrder[0] : 0);

        PdfDictionary streamDict = new();
        streamDict.Set(PdfName.Intern("Length"), hintBody.Length);
        streamDict.Set(PdfName.Intern("S"), 0);  // shared object section absent

        PdfStream stream = new PdfStream(streamDict, hintBody);
        return SerialiseObject(new PdfIndirectObject(new PdfObjectId(objNum, 0), stream));
    }

    private static PdfDictionary CloneDict(PdfDictionary src)
    {
        PdfDictionary copy = new();
        foreach (KeyValuePair<PdfName, PdfPrimitive> kvp in src)
        {
            copy.Set(kvp.Key, kvp.Value);
        }
        return copy;
    }

    private static int FindHighestObjectNumber(
        int paramDictObjNum, int hintStreamObjNum, IList<PdfIndirectObject> objects)
    {
        int max = Math.Max(paramDictObjNum, hintStreamObjNum);
        foreach (PdfIndirectObject o in objects)
        {
            if (o.Id.ObjectNumber > max) { max = o.Id.ObjectNumber; }
        }
        return max;
    }
}
