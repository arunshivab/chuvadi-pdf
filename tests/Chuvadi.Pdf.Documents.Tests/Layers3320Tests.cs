// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-208 item 5, D-230 Q6/Q7) — layer Id, IsLocked and
//        id-keyed overrides

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class Layers3320Tests
{
    // Objects 10 and 11 are both named "Notes"; 11 is locked. 12 is "Grid".
    private static readonly PdfReference NotesA = new PdfReference(new PdfObjectId(10, 0));
    private static readonly PdfReference NotesB = new PdfReference(new PdfObjectId(11, 0));
    private static readonly PdfReference Grid = new PdfReference(new PdfObjectId(12, 0));

    [Fact]
    public void Groups_HaveTheirObjectNumberAsId_AndTheLockedFlag()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        IReadOnlyList<OptionalContentGroup> groups = OptionalContentReader.GetGroups(doc);

        groups.Select(g => (g.Name, g.Id, g.IsLocked)).Should().Equal(
            ("Notes", 10, false),
            ("Notes", 11, true),
            ("Grid", 12, false));
    }

    [Fact]
    public void OverrideById_SetsApartLayersWithTheSameName()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        OptionalContentVisibility v = doc.Pages[0].OptionalContent
            .WithOverridesById(new Dictionary<int, bool> { [10] = false });

        v.IsVisible(NotesA).Should().BeFalse();
        v.IsVisible(NotesB).Should().BeTrue();
        v.IsVisible(Grid).Should().BeFalse("Grid is OFF in the default configuration and not overridden");
    }

    [Fact]
    public void OverrideById_WinsOverTheNameOverride_InEitherOrder()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        OptionalContentVisibility baseline = doc.Pages[0].OptionalContent;
        Dictionary<string, bool> byName = new Dictionary<string, bool> { ["Notes"] = false };
        Dictionary<int, bool> byId = new Dictionary<int, bool> { [10] = true };

        foreach (OptionalContentVisibility v in new[]
        {
            baseline.WithOverrides(byName).WithOverridesById(byId),
            baseline.WithOverridesById(byId).WithOverrides(byName),
        })
        {
            v.IsVisible(NotesA).Should().BeTrue();
            v.IsVisible(NotesB).Should().BeFalse();
        }
    }

    [Fact]
    public void Overrides_ApplyToLockedLayers()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        OptionalContentVisibility v = doc.Pages[0].OptionalContent
            .WithOverridesById(new Dictionary<int, bool> { [11] = false });

        v.IsVisible(NotesB).Should().BeFalse();
    }

    [Fact]
    public void NoOverrides_KeepTheDefaultConfiguration()
    {
        using PdfDocument doc = PdfDocument.Open(Build(), leaveOpen: false);
        OptionalContentVisibility v = doc.Pages[0].OptionalContent;

        v.WithOverridesById(null).Should().BeSameAs(v);
        v.WithOverridesById(new Dictionary<int, bool>()).Should().BeSameAs(v);
        v.IsVisible(NotesA).Should().BeTrue();
        v.IsVisible(Grid).Should().BeFalse();
    }

    private static MemoryStream Build()
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(new PdfObjectId(2, 0)));

        PdfDictionary config = new PdfDictionary();
        config.Set(PdfName.Intern("OFF"), new PdfArray(new PdfPrimitive[] { Grid }));
        config.Set(PdfName.Intern("Locked"), new PdfArray(new PdfPrimitive[] { NotesB }));
        PdfDictionary ocProps = new PdfDictionary();
        ocProps.Set(PdfName.Intern("OCGs"), new PdfArray(new PdfPrimitive[] { NotesA, NotesB, Grid }));
        ocProps.Set(PdfName.Intern("D"), config);
        catalog.Set(PdfName.Intern("OCProperties"), ocProps);

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(new PdfObjectId(3, 0)) }));
        pages.Set(PdfName.Count, 1);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(new PdfObjectId(2, 0)));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200) }));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(10, 0), Ocg("Notes")),
            new PdfIndirectObject(new PdfObjectId(11, 0), Ocg("Notes")),
            new PdfIndirectObject(new PdfObjectId(12, 0), Ocg("Grid")),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(1, 0)));
        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        ms.Position = 0;
        return ms;
    }

    private static PdfDictionary Ocg(string name)
    {
        PdfDictionary ocg = new PdfDictionary();
        ocg.Set(PdfName.Type, PdfName.Intern("OCG"));
        ocg.Set(PdfName.Intern("Name"), new PdfString(Encoding.ASCII.GetBytes(name)));
        return ocg;
    }
}
