// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.34.0 (D-335 item 7b) — write paths honour the ambient
//        PdfWorkScope and the source documents' PdfLimits, and report
//        unexpected failures as PdfCorruptionException.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Runs a write path (fill, merge, stamp, redact, save, ...) inside a
/// <see cref="PdfWorkScope"/> carrying the limits each source document was
/// opened with, nested in any scope the caller opened.
/// </summary>
/// <remarks>
/// Failures already documented by the write paths pass through unchanged:
/// every <see cref="PdfException"/>, cancellation, argument and
/// invalid-operation errors (caller mistakes), and I/O errors from the
/// output stream. Anything else (an invalid cast, a missing key, an index out
/// of range, a truncated stream) comes from damaged input and is reported as
/// <see cref="PdfCorruptionException"/> with the original as its inner
/// exception.
/// </remarks>
internal static class PdfWriteGuard
{
    /// <summary>Runs <paramref name="work"/> for one source document.</summary>
    internal static void Run(PdfDocument document, string action, Action work) =>
        Run(new[] { document }, action, () =>
        {
            work();
            return true;
        });

    /// <summary>Runs <paramref name="work"/> for one source document and returns its result.</summary>
    internal static T Run<T>(PdfDocument document, string action, Func<T> work) =>
        Run(new[] { document }, action, work);

    /// <summary>Runs <paramref name="work"/> for several source documents.</summary>
    internal static void Run(IEnumerable<PdfDocument> documents, string action, Action work) =>
        Run(documents, action, () =>
        {
            work();
            return true;
        });

    /// <summary>
    /// Runs <paramref name="work"/> for several source documents and returns
    /// its result. Each distinct limits object opens one nested scope.
    /// </summary>
    internal static T Run<T>(IEnumerable<PdfDocument> documents, string action, Func<T> work) =>
        Run(documents, action, work, passThrough: null);

    /// <summary>
    /// Runs <paramref name="work"/> for several source documents and returns
    /// its result; <paramref name="passThrough"/> names further documented
    /// exception types (for assemblies whose own exceptions are not
    /// <see cref="PdfException"/>s).
    /// </summary>
    internal static T Run<T>(IEnumerable<PdfDocument> documents, string action, Func<T> work, Func<Exception, bool>? passThrough)
    {
        List<PdfWorkScope> scopes = new List<PdfWorkScope>();
        HashSet<PdfLimits> seen = new HashSet<PdfLimits>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (PdfDocument document in documents ?? Array.Empty<PdfDocument>())
            {
                if (document?.Objects.Limits is PdfLimits limits && seen.Add(limits))
                {
                    scopes.Add(PdfWorkScope.Enter(limits));
                }
            }

            if (scopes.Count == 0)
            {
                scopes.Add(PdfWorkScope.Enter(null));
            }

            return work();
        }
        catch (Exception ex) when (IsUnexpected(ex) && passThrough?.Invoke(ex) != true)
        {
            throw new PdfCorruptionException("The document could not be " + action + ": " + ex.Message, ex);
        }
        finally
        {
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }
        }
    }

    /// <summary>
    /// True for failures that are not part of any write path's documented
    /// set, so that they come from damaged input.
    /// </summary>
    internal static bool IsUnexpected(Exception ex) =>
        ex is not (PdfException or OperationCanceledException or OutOfMemoryException
            or ObjectDisposedException or ArgumentException or InvalidOperationException)
        && (ex is not System.IO.IOException || ex is System.IO.EndOfStreamException);
}
