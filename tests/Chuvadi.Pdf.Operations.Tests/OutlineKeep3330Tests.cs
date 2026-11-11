// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 (document outline), §12.3.2 (destinations),
//        §12.6 (actions)
// PHASE: 3.33.0 (PDF-17, D-322) — saving edited bookmarks keeps the targets,
//        colour, style and open state of the ones kept from the outline.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class OutlineKeep3330Tests
{
    private static readonly string[] KeptKeys = { "Dest", "A", "C", "F", "SE" };

    [Fact]
    public void KeptBookmarks_RenamedOrNot_WriteBackTheirTargetsAndAppearanceUnchanged()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(SourceFile()));
        IReadOnlyList<OutlineItem> items = OutlineReader.GetOutlines(source);

        // Rename the first bookmark, keep everything else.
        List<OutlineEntry> entries = items
            .Select((item, i) => Keep(item, i == 0 ? "Renamed" : item.Title))
            .ToList();
        using PdfDocument output = Write(source, entries);

        List<PdfDictionary> before = Items(source);
        List<PdfDictionary> after = Items(output);
        after.Should().HaveCount(before.Count);
        for (int i = 0; i < before.Count; i++)
        {
            foreach (string key in KeptKeys)
            {
                Serialized(output, after[i], key).Should().Be(Serialized(source, before[i], key), $"item {i} /{key}");
            }
        }

        OutlineReader.GetOutlines(output)[0].Title.Should().Be("Renamed");
    }

    [Fact]
    public void KeptBookmarks_KeepTheirExactDestinationAndNamedDestination()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(SourceFile()));
        IReadOnlyList<OutlineItem> items = OutlineReader.GetOutlines(source);
        using PdfDocument output = Write(source, items.Select(i => Keep(i, i.Title)).ToList());

        IReadOnlyList<OutlineItem> reread = OutlineReader.GetOutlines(output);
        reread[0].Destination!.Kind.Should().Be(PdfDestinationKind.XYZ);
        reread[0].Destination!.Top.Should().Be(500);
        reread[0].Destination!.Zoom.Should().Be(2);
        reread[1].Destination!.PageIndex.Should().Be(1);
        Items(output)[1].TryGetValue(PdfName.Intern("Dest"), out PdfPrimitive? named).Should().BeTrue();
        named.Should().BeOfType<PdfString>("the named destination stays named");
    }

    [Fact]
    public void KeptOpenBookmark_StaysOpen_AndCountsItsVisibleChildren()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(SourceFile()));
        IReadOnlyList<OutlineItem> items = OutlineReader.GetOutlines(source);
        using PdfDocument output = Write(source, items.Select(i => Keep(i, i.Title)).ToList());

        PdfDictionary parent = Items(output).Single(d => d.ContainsKey(PdfName.Intern("First")));
        ((PdfInteger)parent[PdfName.Count]).Value.Should().Be(2, "it was open with two children");
        PdfDictionary root = output.Objects.ResolveAs<PdfDictionary>(output.Catalog[PdfName.Outlines])!;
        ((PdfInteger)root[PdfName.Count]).Value.Should().Be(items.Count + 2);
    }

    [Fact]
    public void ExactDestination_IsWrittenAsGiven()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(SourceFile()));
        PdfDestination fitR = new PdfDestination(1, PdfDestinationKind.FitR) { Left = 10, Bottom = 20, Right = 300, Top = 400 };
        PdfDestination xyz = new PdfDestination(0, PdfDestinationKind.XYZ) { Left = 72, Top = 700 };

        using PdfDocument output = Write(source, new List<OutlineEntry>
        {
            new OutlineEntry("Rect", fitR, Array.Empty<OutlineEntry>()),
            new OutlineEntry("Heading", xyz, Array.Empty<OutlineEntry>()),
        });

        IReadOnlyList<OutlineItem> reread = OutlineReader.GetOutlines(output);
        reread[0].Destination!.Kind.Should().Be(PdfDestinationKind.FitR);
        reread[0].Destination!.Right.Should().Be(300);
        reread[0].Destination!.PageIndex.Should().Be(1);
        reread[1].Destination!.Top.Should().Be(700);
        reread[1].Destination!.Zoom.Should().BeNull("a null zoom is written as null");
        PdfArray dest = (PdfArray)Items(output)[1][PdfName.Intern("Dest")];
        dest[4].Should().BeOfType<PdfNull>();
    }

    [Fact]
    public void PageIndexEntry_IsUnchanged_WholePageAndClosed()
    {
        using PdfDocument source = PdfDocument.Open(new MemoryStream(SourceFile()));

        using PdfDocument output = Write(source, new List<OutlineEntry>
        {
            new OutlineEntry("Parent", 1, new[] { new OutlineEntry("Child", 0) }),
        });

        PdfDictionary parent = Items(output)[0];
        PdfArray dest = (PdfArray)parent[PdfName.Intern("Dest")];
        ((PdfName)dest[1]).Value.Should().Be("Fit");
        ((PdfInteger)parent[PdfName.Count]).Value.Should().Be(-1);
    }

    [Fact]
    public void BookmarkFromAnotherDocument_Throws()
    {
        using PdfDocument first = PdfDocument.Open(new MemoryStream(SourceFile()));
        using PdfDocument second = PdfDocument.Open(new MemoryStream(SourceFile()));
        OutlineItem foreign = OutlineReader.GetOutlines(first)[0];

        using MemoryStream output = new MemoryStream();
        FluentActions.Invoking(() => OutlineWriter.Apply(output, second, new[] { Keep(foreign, "x") }))
            .Should().Throw<ArgumentException>();
    }

    private static OutlineEntry Keep(OutlineItem item, string title) =>
        new OutlineEntry(item, title, item.Children.Select(c => Keep(c, c.Title)).ToList());

    private static PdfDocument Write(PdfDocument source, IReadOnlyList<OutlineEntry> entries)
    {
        using MemoryStream output = new MemoryStream();
        OutlineWriter.Apply(output, source, entries);
        return PdfDocument.Open(new MemoryStream(output.ToArray()));
    }

    // Every outline item, depth first.
    private static List<PdfDictionary> Items(PdfDocument document)
    {
        List<PdfDictionary> result = new List<PdfDictionary>();
        PdfDictionary root = document.Objects.ResolveAs<PdfDictionary>(document.Catalog[PdfName.Outlines])!;
        Walk(document, root.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? first) ? first : null, result);
        return result;
    }

    private static void Walk(PdfDocument document, PdfPrimitive? current, List<PdfDictionary> result)
    {
        while (current is not null && document.Objects.ResolveAs<PdfDictionary>(current) is PdfDictionary item)
        {
            result.Add(item);
            Walk(document, item.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? child) ? child : null, result);
            current = item.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? next) ? next : null;
        }
    }

    // The value of an entry, resolved and written as PDF text, or "-" when absent.
    private static string Serialized(PdfDocument document, PdfDictionary item, string key)
    {
        if (!item.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value))
        {
            return "-";
        }

        PdfPrimitive resolved = document.Objects.Resolve(value);
        return resolved.ToString() ?? string.Empty;
    }

    // Two pages; an outline of: /XYZ with zoom and blue bold style; a named
    // destination; a URI action; a GoToR action with a structure element
    // link; an open parent with two children (one /FitH, one closed).
    private static byte[] SourceFile()
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfObjectId Id(int n) => new PdfObjectId(n, 0);
        PdfReference Ref(int n) => new PdfReference(Id(n));
        PdfDictionary Dict(params (string Key, PdfPrimitive Value)[] entries)
        {
            PdfDictionary d = new PdfDictionary();
            foreach ((string key, PdfPrimitive value) in entries)
            {
                d.Set(PdfName.Intern(key), value);
            }

            return d;
        }

        PdfArray Arr(params PdfPrimitive[] items) => new PdfArray(items);
        PdfString Str(string s) => new PdfString(System.Text.Encoding.ASCII.GetBytes(s));
        PdfArray Box() => Arr(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792));

        objects.Add(new PdfIndirectObject(Id(1), Dict(("Type", PdfName.Catalog), ("Pages", Ref(2)), ("Outlines", Ref(10)),
            ("Names", Dict(("Dests", Dict(("Names", Arr(Str("ch2"), Arr(Ref(4), PdfName.Intern("Fit")))))))))));
        objects.Add(new PdfIndirectObject(Id(2), Dict(("Type", PdfName.Pages), ("Kids", Arr(Ref(3), Ref(4))), ("Count", new PdfInteger(2)))));
        objects.Add(new PdfIndirectObject(Id(3), Dict(("Type", PdfName.Page), ("Parent", Ref(2)), ("MediaBox", Box()))));
        objects.Add(new PdfIndirectObject(Id(4), Dict(("Type", PdfName.Page), ("Parent", Ref(2)), ("MediaBox", Box()))));
        objects.Add(new PdfIndirectObject(Id(9), Dict(("S", PdfName.Intern("URI")), ("URI", Str("https://example.org/")))));

        objects.Add(new PdfIndirectObject(Id(10), Dict(("Type", PdfName.Outlines), ("First", Ref(11)), ("Last", Ref(15)), ("Count", new PdfInteger(7)))));
        objects.Add(new PdfIndirectObject(Id(11), Dict(("Title", Str("Heading")), ("Parent", Ref(10)), ("Next", Ref(12)),
            ("Dest", Arr(Ref(3), PdfName.Intern("XYZ"), new PdfInteger(72), new PdfInteger(500), new PdfInteger(2))),
            ("C", Arr(new PdfInteger(0), new PdfInteger(0), new PdfInteger(1))), ("F", new PdfInteger(2)))));
        objects.Add(new PdfIndirectObject(Id(12), Dict(("Title", Str("Named")), ("Parent", Ref(10)), ("Prev", Ref(11)), ("Next", Ref(13)),
            ("Dest", Str("ch2")))));
        objects.Add(new PdfIndirectObject(Id(13), Dict(("Title", Str("Web")), ("Parent", Ref(10)), ("Prev", Ref(12)), ("Next", Ref(14)),
            ("A", Ref(9)))));
        objects.Add(new PdfIndirectObject(Id(14), Dict(("Title", Str("Other file")), ("Parent", Ref(10)), ("Prev", Ref(13)), ("Next", Ref(15)),
            ("A", Dict(("S", PdfName.Intern("GoToR")), ("F", Str("other.pdf")), ("D", Arr(new PdfInteger(0), PdfName.Intern("Fit"))))),
            ("SE", Ref(3)))));
        objects.Add(new PdfIndirectObject(Id(15), Dict(("Title", Str("Open parent")), ("Parent", Ref(10)), ("Prev", Ref(14)),
            ("First", Ref(16)), ("Last", Ref(17)), ("Count", new PdfInteger(2)),
            ("Dest", Arr(Ref(4), PdfName.Intern("Fit"))))));
        objects.Add(new PdfIndirectObject(Id(16), Dict(("Title", Str("Child FitH")), ("Parent", Ref(15)), ("Next", Ref(17)),
            ("Dest", Arr(Ref(4), PdfName.Intern("FitH"), new PdfInteger(300))))));
        objects.Add(new PdfIndirectObject(Id(17), Dict(("Title", Str("Child")), ("Parent", Ref(15)), ("Prev", Ref(16)),
            ("Dest", Arr(Ref(3), PdfName.Intern("Fit"))))));

        PdfDictionary trailer = Dict(("Root", Ref(1)), ("Size", new PdfInteger(18)));
        using MemoryStream stream = new MemoryStream();
        PdfWriter.Write(stream, objects, trailer, null);
        return stream.ToArray();
    }
}
