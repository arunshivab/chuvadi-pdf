// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Exposes internals of Chuvadi.Pdf.Filters to the Filters test assembly so the
// JBIG2 internals (the MQ arithmetic coder, segment readers, region decoders)
// can be exercised directly without routing through synthetic-PDF construction.
// Added in Phase 2 (items 22/23) to support the JBIG2 unit tests.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chuvadi.Pdf.Filters.Tests")]
