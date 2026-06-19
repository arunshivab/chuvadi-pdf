// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.3 — pattern validators and sets
//
// Checksum validators are pinned to published reference vectors: a wrong table
// or weighting would silently pass invalid identifiers or reject valid ones.

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class PatternValidatorsTests
{
    [Theory]
    [InlineData("79927398713", true)]   // Wikipedia Luhn reference
    [InlineData("79927398714", false)]
    [InlineData("4532 0151 1283 0366", true)]
    [InlineData("", false)]
    public void Luhn_MatchesReference(string value, bool expected)
    {
        PatternValidators.Luhn(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("1428570", true)]    // Wikipedia Verhoeff reference (142857 + 0)
    [InlineData("1428571", false)]   // wrong check digit
    [InlineData("1438570", false)]   // mutated payload
    public void Verhoeff_MatchesReference(string value, bool expected)
    {
        PatternValidators.Verhoeff(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("GB82 WEST 1234 5698 7654 32", true)]   // ISO 13616 reference
    [InlineData("GB82WEST12345698765432", true)]
    [InlineData("GB83WEST12345698765432", false)]
    public void Iban_MatchesReference(string value, bool expected)
    {
        PatternValidators.Iban(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("021000021", true)]   // valid US routing checksum
    [InlineData("021000022", false)]
    [InlineData("12345678", false)]   // wrong length
    public void AbaRouting_MatchesReference(string value, bool expected)
    {
        PatternValidators.AbaRouting(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234567893", true)]   // CMS NPI reference
    [InlineData("1234567890", false)]
    public void Npi_MatchesReference(string value, bool expected)
    {
        PatternValidators.Npi(value).Should().Be(expected);
    }

    [Fact]
    public void Validators_RejectNull()
    {
        Action act = () => PatternValidators.Luhn(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
