// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Exposes internals of Chuvadi.Pdf.IO to the IO test assembly so that
// internal helpers like ObjectStreamReader.Decode can be exercised
// directly without going through synthetic-PDF construction.
// Added in v2.1.8 to support ChainedFilterDecodeTests.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chuvadi.Pdf.IO.Tests")]

// 3.32.1 (D-280): PdfDocument.Open uses PdfReader.OpenRepaired for
// PdfOpenOptions.RepairIfDamaged.
[assembly: InternalsVisibleTo("Chuvadi.Pdf.Documents")]
