// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335 item 7b) — write paths honour the ambient
//        PdfWorkScope and the document's PdfLimits, and report unexpected
//        failures as PdfCorruptionException.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Redaction;
using Chuvadi.Pdf.Watermark;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class WritePathGuard3340Tests
{
    private const string NoResources = "<<>>";
    private const string Content = "0 0 100 100 re f 10 10 50 50 re f 20 20 30 30 re f";

    public static IEnumerable<object[]> WritePaths() => new List<object[]>
    {
        new object[] { "save", new Action<PdfDocument, Stream>((d, o) => d.Save(o)) },
        new object[] { "fill", new Action<PdfDocument, Stream>((d, o) => FormFiller.Fill(o, d, new Dictionary<string, string>())) },
        new object[] { "merge", new Action<PdfDocument, Stream>((d, o) => PageOperations.Merge(o, d, d)) },
        new object[] { "watermark", new Action<PdfDocument, Stream>((d, o) => WatermarkStamper.ApplyText(o, d, new TextWatermarkOptions("X"))) },
        new object[] { "header", new Action<PdfDocument, Stream>((d, o) => HeaderFooter.Apply(o, d, new HeaderFooterOptions { Footer = new BandText(center: "p") })) },
        new object[] { "compress", new Action<PdfDocument, Stream>((d, o) => PdfCompressor.Compress(d, o)) },
        new object[] { "redact", new Action<PdfDocument, Stream>((d, o) => Redactor.Apply(o, d, new RedactionOptions())) },
    };

    [Theory]
    [MemberData(nameof(WritePaths))]
    public void WritePath_InACancelledScope_Throws_OperationCanceled(string name, Action<PdfDocument, Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(Content, NoResources, Array.Empty<string>()));
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        using MemoryStream output = new MemoryStream();

        Action act = () =>
        {
            using (PdfWorkScope.Enter(null, cts.Token))
            {
                write(doc, output);
            }
        };

        act.Should().Throw<OperationCanceledException>(name);
    }

    [Theory]
    [MemberData(nameof(WritePaths))]
    public void WritePath_OutsideAnyScope_Succeeds(string name, Action<PdfDocument, Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(Content, NoResources, Array.Empty<string>()));
        using MemoryStream output = new MemoryStream();

        write(doc, output);

        output.Length.Should().BeGreaterThan(0, name);
        PdfWorkScope.Current.Should().BeNull("the write path closes the scopes it opened");
    }

    [Fact]
    public void WritePath_HonoursTheLimitsTheDocumentWasOpenedWith()
    {
        // A Flate content stream that decodes to about 2 KB, opened with
        // MaxDecodedStreamBytes = 400 (enough for the small xref stream):
        // before 3.34.0 the redactor decoded it without any limit.
        string big = string.Concat(System.Linq.Enumerable.Repeat(Content + "\n", 40));
        byte[] pdf = TestPdf.OnePage(big, NoResources, Array.Empty<string>());
        using PdfDocument plain = TestPdf.Open(pdf);
        byte[] flated;
        using (MemoryStream compressed = new MemoryStream())
        {
            PdfCompressor.Compress(plain, compressed);
            flated = compressed.ToArray();
        }

        using PdfDocument limited = PdfDocument.Open(
            new MemoryStream(flated),
            new PdfOpenOptions { Limits = new PdfLimits { MaxDecodedStreamBytes = 400 } });
        using MemoryStream output = new MemoryStream();

        Action redact = () => Redactor.Apply(output, limited, new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, new Chuvadi.Pdf.Graphics.RectangleF(0, 0, 5, 5)) },
        });

        redact.Should().Throw<PdfLimitExceededException>().WithMessage("MaxDecodedStreamBytes*");
    }

    [Fact]
    public void Guard_EntersTheDocumentsLimits_AndRestoresTheCallersScope()
    {
        PdfLimits limits = new PdfLimits { MaxContentOperators = 1000 };
        using PdfDocument doc = PdfDocument.Open(
            new MemoryStream(TestPdf.OnePage(Content, NoResources, Array.Empty<string>())),
            new PdfOpenOptions { Limits = limits });
        using PdfWorkScope outer = PdfWorkScope.Enter(null);

        PdfLimits? seen = PdfWriteGuard.Run(doc, "written", () => PdfWorkScope.Current!.Limits);

        seen.Should().BeSameAs(limits);
        PdfWorkScope.Current.Should().BeSameAs(outer);
    }

    public static IEnumerable<object[]> Wrapped() => new List<object[]>
    {
        new object[] { new InvalidCastException("cast") },
        new object[] { new DivideByZeroException("zero") },
        new object[] { new InvalidDataException("data") },
        new object[] { new KeyNotFoundException("key") },
        new object[] { new OverflowException("overflow") },
        new object[] { new FormatException("format") },
        new object[] { new EndOfStreamException("eos") },
    };

    [Theory]
    [MemberData(nameof(Wrapped))]
    public void Guard_ReportsUnexpectedFailures_AsCorruption(Exception thrown)
    {
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(Content, NoResources, Array.Empty<string>()));

        Action act = () => PdfWriteGuard.Run(doc, "filled", () => throw thrown);

        act.Should().Throw<PdfCorruptionException>()
            .WithMessage("The document could not be filled: *")
            .Which.InnerException.Should().BeSameAs(thrown);
    }

    public static IEnumerable<object[]> PassedThrough() => new List<object[]>
    {
        new object[] { new FormException("form") },
        new object[] { new PdfLimitExceededException("limit") },
        new object[] { new OperationCanceledException() },
        new object[] { new ArgumentOutOfRangeException("x") },
        new object[] { new InvalidOperationException("state") },
        new object[] { new IOException("disk full") },
    };

    [Theory]
    [MemberData(nameof(PassedThrough))]
    public void Guard_LeavesDocumentedFailuresAlone(Exception thrown)
    {
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(Content, NoResources, Array.Empty<string>()));

        Action act = () => PdfWriteGuard.Run(doc, "filled", () => throw thrown);

        act.Should().Throw<Exception>().Which.Should().BeSameAs(thrown);
    }

    [Fact]
    public void Guard_PassThroughPredicate_KeepsAnAssemblysOwnExceptions()
    {
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(Content, NoResources, Array.Empty<string>()));
        NotSupportedException own = new NotSupportedException("own");

        Action act = () => PdfWriteGuard.Run(new[] { doc }, "rendered", new Func<bool>(() => throw own), ex => ex is NotSupportedException);

        act.Should().Throw<NotSupportedException>().Which.Should().BeSameAs(own);
    }
}
