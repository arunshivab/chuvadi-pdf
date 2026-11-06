// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 (D-210 A, D-219) — names compare by value; the shared name
//        table is bounded

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Primitives.Tests;

public sealed class PdfName3312Tests
{
    [Fact]
    public void WellKnownNames_AreShared()
    {
        PdfName.Intern("Type").Should().BeSameAs(PdfName.Type);
        PdfName.Intern("MediaBox").Should().BeSameAs(PdfName.MediaBox);
        PdfName.Intern("ByteRange").Should().BeSameAs(PdfName.Intern("ByteRange"));
    }

    [Fact]
    public void LongNames_AreNotShared_ButCompareEqual()
    {
        string value = new string('L', 65) + Guid.NewGuid().ToString("N");
        PdfName a = PdfName.Intern(value);
        PdfName b = PdfName.Intern(value);

        a.Should().NotBeSameAs(b);
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.Equals((object)b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void UnsharedNames_WorkAsDictionaryKeys()
    {
        string value = new string('K', 70) + Guid.NewGuid().ToString("N");
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern(value), 7);

        dict.TryGetValue(PdfName.Intern(value), out PdfPrimitive? found).Should().BeTrue();
        found.Should().BeOfType<PdfInteger>().Which.Value.Should().Be(7);
        dict.ContainsKey(PdfName.Intern(value)).Should().BeTrue();
    }

    [Fact]
    public void DifferentNames_AreNotEqual()
    {
        PdfName a = PdfName.Intern("Alpha" + Guid.NewGuid().ToString("N"));
        PdfName b = PdfName.Intern("Beta" + Guid.NewGuid().ToString("N"));

        (a == b).Should().BeFalse();
        a.Equals(b).Should().BeFalse();
        (a == null).Should().BeFalse();
        ((PdfName?)null == null).Should().BeTrue();
    }

    [Fact]
    public void TheSharedTableStopsGrowing_AfterTheCap()
    {
        // 5,000 new short names: more than the 4,096 the table takes, however
        // many other tests have added. Once it is full, each new name is a
        // fresh instance, and still equal to its twin.
        string prefix = "Cap" + Guid.NewGuid().ToString("N").Substring(0, 8);
        List<PdfName> last = new List<PdfName>();
        for (int i = 0; i < 5000; i++)
        {
            PdfName n = PdfName.Intern(prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (i >= 4990)
            {
                last.Add(n);
            }
        }

        foreach (PdfName n in last)
        {
            PdfName again = PdfName.Intern(n.Value);
            again.Should().NotBeSameAs(n);
            again.Should().Be(n);
        }
    }
}
