// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

// ── RedactionException ─────────────────────────────────────────────────────

public sealed class RedactionExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        RedactionException ex = new RedactionException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        RedactionException ex = new RedactionException("PHI leak detected");
        ex.Message.Should().Be("PHI leak detected");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        RedactionException ex = new RedactionException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── RedactionRect ──────────────────────────────────────────────────────────

public sealed class RedactionRectTests
{
    [Fact]
    public void Constructor_NegativePageIndex_Throws()
    {
        Action act = () => new RedactionRect(-1, new RectangleF(0, 0, 10, 10));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_SetsFields()
    {
        RedactionRect r = new RedactionRect(2, new RectangleF(10, 20, 100, 50));
        r.PageIndex.Should().Be(2);
        r.Bounds.X.Should().Be(10);
        r.Bounds.Width.Should().Be(100);
    }
}

// ── RedactionOptions ───────────────────────────────────────────────────────

public sealed class RedactionOptionsTests
{
    [Fact]
    public void Default_HasEmptyList()
    {
        RedactionOptions opts = new RedactionOptions();
        opts.Rectangles.Should().BeEmpty();
    }

    [Fact]
    public void Default_OverlayIsBlack()
    {
        RedactionOptions opts = new RedactionOptions();
        opts.OverlayColor.Should().Be(ColorF.Black);
    }
}

// ── Redactor ───────────────────────────────────────────────────────────────

public sealed class RedactorTests
{
    [Fact]
    public void Apply_NullOutput_Throws()
    {
        using (PdfDocument doc = OpenSimpleDoc())
        {
            Action act = () => Redactor.Apply(null!, doc, new RedactionOptions());
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void Apply_NullDocument_Throws()
    {
        Action act = () => Redactor.Apply(new MemoryStream(), null!, new RedactionOptions());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_NullOptions_Throws()
    {
        using (PdfDocument doc = OpenSimpleDoc())
        {
            Action act = () => Redactor.Apply(new MemoryStream(), doc, null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public void Apply_NoRedactions_ProducesValidPdf()
    {
        using (MemoryStream source = BuildTextPdf("BT /F1 12 Tf 100 100 Td (HELLO) Tj ET"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            Redactor.Apply(output, doc, new RedactionOptions());
            output.Length.Should().BeGreaterThan(0);
            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                result.PageCount.Should().Be(1);
            }
        }
    }

    [Fact]
    public void Apply_TextInRedactRect_RemovesTextFromContentStream()
    {
        // Build PDF with text at (100,100) in a 200x200 page
        using (MemoryStream source = BuildTextPdf("BT /F1 12 Tf 100 100 Td (SSN_123_45_6789) Tj ET"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            RedactionOptions opts = new RedactionOptions
            {
                Rectangles = new List<RedactionRect>
                {
                    // Cover text at (100,100): wide rect
                    new RedactionRect(0, new RectangleF(90, 90, 150, 30)),
                },
            };
            Redactor.Apply(output, doc, opts);

            output.Seek(0, SeekOrigin.Begin);
            string outputText = Encoding.Latin1.GetString(output.ToArray());

            // The redacted text MUST NOT appear in the output bytes
            outputText.Should().NotContain("SSN_123_45_6789",
                "redacted text must be permanently removed from content stream");
        }
    }

    [Fact]
    public void Apply_TextOutsideRedactRect_PreservesText()
    {
        using (MemoryStream source = BuildTextPdf("BT /F1 12 Tf 100 100 Td (KEEP_ME) Tj ET"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            RedactionOptions opts = new RedactionOptions
            {
                Rectangles = new List<RedactionRect>
                {
                    // Redact a far-away corner that doesn't overlap text at (100,100)
                    new RedactionRect(0, new RectangleF(500, 500, 50, 50)),
                },
            };
            Redactor.Apply(output, doc, opts);

            output.Seek(0, SeekOrigin.Begin);
            string outputText = Encoding.Latin1.GetString(output.ToArray());

            // Text outside any redaction must still be present
            outputText.Should().Contain("KEEP_ME",
                "text outside redaction rectangles must be preserved");
        }
    }

    [Fact]
    public void Apply_TextRedaction_AddsOverlayRectangle()
    {
        using (MemoryStream source = BuildTextPdf("BT /F1 12 Tf 100 100 Td (PHI) Tj ET"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            RedactionOptions opts = new RedactionOptions
            {
                Rectangles = new List<RedactionRect>
                {
                    new RedactionRect(0, new RectangleF(90, 90, 150, 30)),
                },
            };
            Redactor.Apply(output, doc, opts);

            output.Seek(0, SeekOrigin.Begin);
            string outputText = Encoding.Latin1.GetString(output.ToArray());

            // Overlay stream contains 're' (rectangle) and 'f' (fill) operators
            outputText.Should().Contain(" re",
                "overlay rectangle must be appended to redacted page");
        }
    }

    [Fact]
    public void Apply_MultiplePages_OnlyRedactsTargetedPage()
    {
        using (MemoryStream source = BuildMultiPagePdfWithText(3, "SECRET"))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            RedactionOptions opts = new RedactionOptions
            {
                Rectangles = new List<RedactionRect>
                {
                    // Only redact page 1 (middle page)
                    new RedactionRect(1, new RectangleF(0, 0, 600, 600)),
                },
            };
            Redactor.Apply(output, doc, opts);

            output.Seek(0, SeekOrigin.Begin);

            using (PdfDocument result = PdfDocument.Open(output, leaveOpen: true))
            {
                result.PageCount.Should().Be(3);
            }
        }
    }

    // ── Builder helpers ────────────────────────────────────────────────────

    private static PdfDocument OpenSimpleDoc()
    {
        return PdfDocument.Open(BuildTextPdf("BT /F1 12 Tf 100 100 Td (X) Tj ET"), leaveOpen: false);
    }

    [Fact]
    public void Apply_ParallelOutput_IsByteIdenticalToSequential()
    {
        byte[] sequential = RunRedaction(maxDegreeOfParallelism: 1);
        byte[] parallel = RunRedaction(maxDegreeOfParallelism: -1);

        parallel.Should().Equal(sequential);
    }

    private static byte[] RunRedaction(int maxDegreeOfParallelism)
    {
        using MemoryStream source = BuildMultiPageTextPdf(4);
        source.Seek(0, SeekOrigin.Begin);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);

        RedactionOptions options = new RedactionOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        };
        for (int i = 0; i < 4; i++)
        {
            options.Rectangles.Add(new RedactionRect(i, new RectangleF(90, 90, 200, 30)));
        }

        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, options);
        return output.ToArray();
    }

    private static MemoryStream BuildMultiPageTextPdf(int pageCount)
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        List<PdfPrimitive> kids = new List<PdfPrimitive>();
        int objNum = 3;

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(objNum++, 0);
            PdfObjectId streamId = new PdfObjectId(objNum++, 0);

            byte[] contentBytes = Encoding.Latin1.GetBytes(
                "BT /F1 12 Tf 100 100 Td (SECRET_" + i + ") Tj ET");
            PdfDictionary streamDict = new PdfDictionary();
            streamDict.Set(PdfName.Length, contentBytes.Length);
            objects.Add(new PdfIndirectObject(streamId, new PdfStream(streamDict, contentBytes)));

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Contents, new PdfReference(streamId));
            pageDict.Set(PdfName.MediaBox, new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(612), new PdfInteger(792),
            ]));
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(kids));
        pagesDict.Set(PdfName.Count, pageCount);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        objects.Insert(0, new PdfIndirectObject(pagesId, pagesDict));
        objects.Insert(0, new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    [Fact]
    public void Apply_InlineImageInRedactRect_RemovesImageButKeepsSurroundingText()
    {
        string trap = "Q()BTq\u0001\u0002\u0003\u0004\u0005\u0006";
        string content =
            "BT /F1 12 Tf 10 10 Td (KEEPTEXT) Tj ET\n" +
            "q 40 0 0 40 60 60 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID " + trap + " EI Q\n" +
            "BT /F1 12 Tf 10 30 Td (TAILTEXT) Tj ET\n";

        using (MemoryStream source = BuildTextPdf(content))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            RedactionOptions opts = new RedactionOptions
            {
                Rectangles = new List<RedactionRect>
                {
                    new RedactionRect(0, new RectangleF(55, 55, 50, 50)),
                },
            };
            Redactor.Apply(output, doc, opts);

            string outputText = Encoding.Latin1.GetString(output.ToArray());

            outputText.Should().NotContain("BI ", "the inline image must be removed");
            outputText.Should().Contain("KEEPTEXT", "text before the image must survive");
            outputText.Should().Contain("TAILTEXT", "text after the image must survive");
        }
    }

    [Fact]
    public void Apply_InlineImageOutsideRect_IsPreservedAndBinaryDoesNotCorruptParsing()
    {
        string trap = "Q()BTq\u0001\u0002\u0003\u0004\u0005\u0006";
        string content =
            "BT /F1 12 Tf 10 10 Td (KEEPTEXT) Tj ET\n" +
            "q 40 0 0 40 60 60 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID " + trap + " EI Q\n" +
            "BT /F1 12 Tf 10 30 Td (TAILTEXT) Tj ET\n";

        using (MemoryStream source = BuildTextPdf(content))
        using (PdfDocument doc = PdfDocument.Open(source, leaveOpen: true))
        using (MemoryStream output = new MemoryStream())
        {
            RedactionOptions opts = new RedactionOptions
            {
                Rectangles = new List<RedactionRect>
                {
                    new RedactionRect(0, new RectangleF(0, 0, 5, 5)),
                },
            };
            Redactor.Apply(output, doc, opts);

            string outputText = Encoding.Latin1.GetString(output.ToArray());

            outputText.Should().Contain("BI ", "the inline image must be preserved");
            outputText.Should().Contain(" EI", "the inline image must be preserved");
            outputText.Should().Contain("TAILTEXT",
                "binary image data must be skipped as a unit so following text still parses");
        }
    }

    private static MemoryStream BuildTextPdf(string contentStream)
    {
        byte[] contentBytes = Encoding.Latin1.GetBytes(contentStream);

        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId streamId = new PdfObjectId(4, 0);

        PdfDictionary streamDict = new PdfDictionary();
        streamDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream content = new PdfStream(streamDict, contentBytes);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(streamId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(streamId, content),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static MemoryStream BuildMultiPagePdfWithText(int pageCount, string text)
    {
        string contentSnippet = $"BT /F1 12 Tf 100 100 Td ({text}) Tj ET";
        byte[] contentBytes = Encoding.Latin1.GetBytes(contentSnippet);

        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfArray kids = new PdfArray([]);
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        int nextId = 3;

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(nextId++, 0);
            PdfObjectId streamId = new PdfObjectId(nextId++, 0);

            PdfDictionary streamDict = new PdfDictionary();
            streamDict.Set(PdfName.Length, contentBytes.Length);
            objects.Add(new PdfIndirectObject(streamId, new PdfStream(streamDict, contentBytes)));

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Contents, new PdfReference(streamId));
            pageDict.Set(PdfName.MediaBox, new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(612), new PdfInteger(792),
            ]));
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageCount);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        objects.Add(new PdfIndirectObject(catalogId, catalogDict));
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}

// ── Phase 1.1.2: Pattern-based redaction ──────────────────────────────────

public sealed class PatternRuleTests
{
    [Fact]
    public void Construct_FromString_CompilesRegex()
    {
        PatternRule rule = new(@"\d{3}-\d{2}-\d{4}");
        rule.Regex.IsMatch("123-45-6789").Should().BeTrue();
        rule.Regex.IsMatch("hello").Should().BeFalse();
    }

    [Fact]
    public void Construct_NullPattern_Throws()
    {
        Action act = () => new PatternRule((string)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_FromRegex_StoresIt()
    {
        System.Text.RegularExpressions.Regex r = new("foo");
        PatternRule rule = new(r);
        rule.Regex.Should().BeSameAs(r);
    }

    [Fact]
    public void AppliesToPage_NoFilter_AlwaysTrue()
    {
        PatternRule rule = new("x");
        rule.AppliesToPage(0).Should().BeTrue();
        rule.AppliesToPage(100).Should().BeTrue();
    }

    [Fact]
    public void AppliesToPage_WithFilter_RestrictsToListed()
    {
        PatternRule rule = new("x", new[] { 1, 3 });
        rule.AppliesToPage(0).Should().BeFalse();
        rule.AppliesToPage(1).Should().BeTrue();
        rule.AppliesToPage(2).Should().BeFalse();
        rule.AppliesToPage(3).Should().BeTrue();
    }
}

public sealed class CommonPatternsTests
{
    [Theory]
    [InlineData(CommonPatterns.UsSsn, "123-45-6789", true)]
    [InlineData(CommonPatterns.UsSsn, "12-45-6789", false)]
    [InlineData(CommonPatterns.Email, "user@example.com", true)]
    [InlineData(CommonPatterns.Email, "not an email", false)]
    [InlineData(CommonPatterns.IsoDate, "2025-12-31", true)]
    [InlineData(CommonPatterns.IsoDate, "31/12/2025", false)]
    public void Pattern_Matches(string pattern, string input, bool expected)
    {
        new System.Text.RegularExpressions.Regex(pattern).IsMatch(input).Should().Be(expected);
    }
}

public sealed class RedactionOptionsExtensionTests
{
    [Fact]
    public void Patterns_DefaultEmpty()
    {
        RedactionOptions opts = new();
        opts.Patterns.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void PatternPadding_DefaultOnePoint()
    {
        RedactionOptions opts = new();
        opts.PatternPadding.Should().Be(1.0);
    }

    [Fact]
    public void Patterns_AddRule()
    {
        RedactionOptions opts = new();
        opts.Patterns.Add(new PatternRule(CommonPatterns.UsSsn));
        opts.Patterns.Should().HaveCount(1);
    }
}

// ── Phase 1.1.3: Image / Form XObject redaction ───────────────────────────

public sealed class ImageRedactionTests
{
    [Fact]
    public void DoOperator_InsideRect_IsDropped()
    {
        // Build a minimal page whose content stream paints an XObject "/Im0"
        // at a CTM that places its unit square inside (100, 700, 200, 50).
        // After redaction with a rect covering (100, 700, 200, 50), the
        // Do operator should be removed from the content stream.
        PdfDocument doc = BuildPageWithDo(
            ctmCm: "100 0 0 50 100 700",  // place image at (100,700) size 100×50
            xobjectName: "/Im0");

        using MemoryStream output = new();
        RedactionOptions opts = new();
        opts.Rectangles.Add(new RedactionRect(0, new RectangleF(80, 680, 240, 100)));

        Redactor.Apply(output, doc, opts);

        byte[] outBytes = output.ToArray();
        string asText = System.Text.Encoding.Latin1.GetString(outBytes);
        asText.Should().NotContain("/Im0 Do",
            "the Do operator that fell inside the redaction rectangle must be removed");
    }

    [Fact]
    public void DoOperator_OutsideRect_IsPreserved()
    {
        PdfDocument doc = BuildPageWithDo(
            ctmCm: "100 0 0 50 100 100",  // image at bottom of page (100, 100)
            xobjectName: "/Im0");

        using MemoryStream output = new();
        RedactionOptions opts = new();
        opts.Rectangles.Add(new RedactionRect(0, new RectangleF(80, 680, 240, 100)));

        Redactor.Apply(output, doc, opts);

        string asText = System.Text.Encoding.Latin1.GetString(output.ToArray());
        asText.Should().Contain("/Im0 Do");
    }

    private static PdfDocument BuildPageWithDo(string ctmCm, string xobjectName)
    {
        // Indirect objects: catalog, pages, page, contents (stream)
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId contentsId = new(4, 0);
        PdfObjectId imId = new(5, 0);

        string streamText = $"q {ctmCm} cm {xobjectName} Do Q";
        byte[] streamBytes = System.Text.Encoding.Latin1.GetBytes(streamText);

        PdfDictionary streamDict = new();
        streamDict.Set(PdfName.Intern("Length"), streamBytes.Length);
        PdfStream contents = new(streamDict, streamBytes);

        // Image XObject dictionary
        PdfDictionary imDict = new();
        imDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        imDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imDict.Set(PdfName.Intern("Width"), 1);
        imDict.Set(PdfName.Intern("Height"), 1);
        imDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceGray"));
        imDict.Set(PdfName.Intern("Length"), 1);
        PdfStream im = new(imDict, new byte[] { 0xFF });

        // Page resources reference the image
        PdfDictionary xobjects = new();
        xobjects.Set(PdfName.Intern("Im0"), new PdfReference(imId));
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));
        pageDict.Set(PdfName.Intern("Resources"), resources);
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentsId));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new(catalogId, catalogDict),
            new(pagesId, pagesDict),
            new(pageId, pageDict),
            new(contentsId, contents),
            new(imId, im),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
