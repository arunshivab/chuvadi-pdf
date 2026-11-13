// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 (cross-reference table), §7.3.10 (null for
//        references to undefined objects)
// PHASE: 3.21 — xref recovery warnings raised during lazy object loads.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Thread-safe collector for structural repairs made while objects load
/// lazily, surfaced through <see cref="PdfReader.Warnings"/>.
/// </summary>
internal sealed class RecoveryLog
{
    private readonly object _sync = new object();
    private readonly List<string> _entries = new List<string>();
    private readonly HashSet<string> _once = new HashSet<string>(StringComparer.Ordinal);

    internal void Add(string message)
    {
        lock (_sync)
        {
            _entries.Add(message);
        }
    }

    // Adds a message the first time it is given; later identical messages
    // are dropped (3.34.0).
    internal void AddOnce(string message)
    {
        lock (_sync)
        {
            if (_once.Add(message))
            {
                _entries.Add(message);
            }
        }
    }

    internal IReadOnlyList<string> Snapshot()
    {
        lock (_sync)
        {
            return _entries.Count == 0 ? Array.Empty<string>() : _entries.ToArray();
        }
    }
}
