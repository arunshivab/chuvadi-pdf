// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.36.0 (D-430) — recursion on untrusted input is iterative, so deeply
//        nested documents cannot exhaust the native call stack. These tests run
//        the deep-nesting paths on a thread with a 256 KB stack: a recursive
//        regression would overflow and crash the test host (an uncatchable
//        StackOverflowException), so Linux CI catches this whole class of bug.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class StackSafety336Tests
{
    private const int SmallStackBytes = 256 * 1024;

    [Fact]
    public void DeeplyNestedDictionaries_RefuseCleanly_OnA256KbStack()
    {
        RunOnSmallStack(() =>
        {
            PdfParseException ex = Assert.Throws<PdfParseException>(() =>
                _ = Open(DeepDictionaries(20_000)).Pages[0].Width);
            ex.Message.Should().Contain("levels deep");
        });
    }

    [Fact]
    public void DeeplyNestedArrays_RefuseCleanly_OnA256KbStack()
    {
        RunOnSmallStack(() =>
        {
            PdfParseException ex = Assert.Throws<PdfParseException>(() =>
                _ = Open(DeepArrays(20_000)).Pages[0].Width);
            ex.Message.Should().Contain("levels deep");
        });
    }

    [Fact]
    public void NestingAtTheLimit_IsStillRead_OnA256KbStack()
    {
        RunOnSmallStack(() =>
        {
            // 510 arrays reach level 512 (the limit): read without overflow.
            using PdfDocument doc = Open(new string('[', 510) + new string(']', 510));
            doc.Pages[0].Width.Should().Be(200);
        });
    }

    [Fact]
    public void DeepPageTree_IsEnumerated_OnA256KbStack()
    {
        RunOnSmallStack(() =>
        {
            // A /Pages tree nested 1000 deep (well within the walks' 1024 bound):
            // enumerating and indexing it must not overflow the stack.
            using PdfDocument doc = PdfDocument.Open(
                new MemoryStream(Encoding.Latin1.GetBytes(DeepPageTree(1000))), new PdfOpenOptions());
            doc.Pages.Count.Should().Be(1);
            doc.Pages[0].Width.Should().Be(200);
        });
    }

    private static string DeepPageTree(int pagesDepth)
    {
        List<string> bodies = new List<string> { "<</Type/Catalog/Pages 2 0 R>>" };
        for (int k = 0; k < pagesDepth; k++)
        {
            int self = 2 + k;
            bodies.Add("<</Type/Pages/Kids[" + (self + 1).ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        }

        int leafParent = 1 + pagesDepth;
        bodies.Add("<</Type/Page/Parent " + leafParent.ToString(CultureInfo.InvariantCulture)
            + " 0 R/MediaBox[0 0 200 200]>>");
        return Assemble(bodies.ToArray());
    }

    // Runs body on a thread with a deliberately small (256 KB) stack and
    // surfaces any ordinary exception on the calling thread. A recursive
    // regression would instead overflow and abort the whole test run.
    private static void RunOnSmallStack(Action body)
    {
        Exception? captured = null;
        Thread thread = new Thread(
            () =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            },
            SmallStackBytes);
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            throw new InvalidOperationException("Body failed on the small-stack thread.", captured);
        }
    }

    private static PdfDocument Open(string resourcesX)
    {
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</X " + resourcesX + ">>/Contents 4 0 R>>",
            "<</Length 11>>\nstream\n0 0 1 1 re\nendstream",
        };
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(Assemble(bodies))), new PdfOpenOptions());
    }

    private static string DeepDictionaries(int depth) =>
        string.Concat(Enumerable.Repeat("<</K ", depth)) + "<<>>" + string.Concat(Enumerable.Repeat(">>", depth));

    private static string DeepArrays(int depth) =>
        new string('[', depth) + new string(']', depth);

    private static string Assemble(string[] bodies)
    {
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Length + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return sb.ToString();
    }
}
