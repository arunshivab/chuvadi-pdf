// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.3 — Text-showing operators (fragment joining)
// PHASE: Chuvadi.Pdf.Text — layout reconstruction spacing.
//
// Many writers (notably Adobe form-field appearance generators) end each
// word-run with an explicit trailing space and advance with Td:
//     (HEALTHCARE ) Tj 82.992 0 Td (SERVICES ) Tj
// Width estimation on wide uppercase runs can under-estimate, making the
// positional gap look like a word break — which used to insert a second
// space ("HEALTHCARE  SERVICES") and break exact-substring searches over
// the extracted text. A separator is only inserted when the fragments do
// not already carry one.

using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class LayoutExtractorSpacingTests
{
    [Fact]
    public void GapAfterTrailingSpaceDoesNotDoubleTheSeparator()
    {
        // Fragment 1 ends with an explicit space; the X gap to fragment 2 is
        // larger than the width estimate would predict.
        List<TextFragment> fragments = new List<TextFragment>
        {
            new TextFragment("HEALTHCARE ", 0, 100, 12),
            new TextFragment("SERVICES", 83, 100, 12),
        };

        string text = new LayoutExtractor().Extract(fragments);

        text.Should().Be("HEALTHCARE SERVICES");
        text.Should().NotContain("  ");
    }

    [Fact]
    public void GapWithoutExistingSeparatorStillInsertsSpace()
    {
        List<TextFragment> fragments = new List<TextFragment>
        {
            new TextFragment("Hello", 0, 100, 12),
            new TextFragment("World", 80, 100, 12),
        };

        string text = new LayoutExtractor().Extract(fragments);

        text.Should().Be("Hello World");
    }
}
