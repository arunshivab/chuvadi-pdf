// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 2)
// Exposes internal types (e.g. the hinting interpreter) to the test assembly so
// internal-only components can be unit-tested without a public surface.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chuvadi.Pdf.Fonts.Rendering.Tests")]
