// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.3 — pattern validators / sets end-to-end
//
// A validator gates redaction: a checksum-invalid match is left in place while
// a valid one is removed. Also covers the labelled-value matcher and the sets.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class PatternRedactionIntegrationTests
{
    [Fact]
    public void Validator_SkipsInvalidChecksum_RedactsValid()
    {
        // Build a Verhoeff-valid 12-digit Aadhaar and an invalid sibling that
        // shares the same 11-digit prefix but a wrong check digit.
        string prefix = "23412345678";
        string valid = string.Empty;
        for (int cd = 0; cd <= 9; cd++)
        {
            string candidate = prefix + cd;
            if (PatternValidators.Verhoeff(candidate))
            {
                valid = candidate;
                break;
            }
        }

        valid.Should().NotBeEmpty("a valid check digit must exist");
        string invalid = prefix + ((valid[11] - '0' + 1) % 10);

        using MemoryStream source = BuildLinesPdf(valid, invalid);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Patterns = new List<PatternRule>
            {
                new PatternRule(CommonPatterns.IndiaAadhaar, null, PatternValidators.Verhoeff),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);

        after.Should().NotContain(valid, "the valid Aadhaar must be redacted");
        after.Should().Contain(invalid, "the checksum-invalid number must be left in place");
    }

    [Fact]
    public void LabeledValue_RedactsLabelAndValue()
    {
        using MemoryStream source = BuildLinesPdf("MRN: 0099123");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Patterns = new List<PatternRule> { CommonPatterns.LabeledValue("MRN") },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
        after.Should().NotContain("0099123");
    }

    [Fact]
    public void PatternSets_AreNonEmptyAndFresh()
    {
        PatternSets.Financial.Should().NotBeEmpty();
        PatternSets.Medical.Should().NotBeEmpty();
        PatternSets.GeneralPii.Should().NotBeEmpty();

        // Each access returns an independent list the caller may mutate.
        ReferenceEquals(PatternSets.Financial, PatternSets.Financial).Should().BeFalse();
    }

    private static MemoryStream BuildLinesPdf(params string[] lines)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));

        PdfDictionary fontResources = new PdfDictionary();
        fontResources.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResources);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);

        StringBuilder cs = new StringBuilder();
        int y = 700;
        foreach (string line in lines)
        {
            cs.Append($"BT /F1 12 Tf 40 {y} Td ({line}) Tj ET\n");
            y -= 30;
        }

        byte[] content = Encoding.ASCII.GetBytes(cs.ToString());
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(fontId, font),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
