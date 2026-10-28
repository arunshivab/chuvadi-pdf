// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A3, A4)

using System;
using System.Threading;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// An ambient cancellation token and set of <see cref="PdfLimits"/> for the
/// Chuvadi work done on the current asynchronous flow until it is disposed.
/// </summary>
/// <remarks>
/// <para>
/// Content walking, text parsing, painting, stream decoding and image
/// decoding check the scope at short intervals: a cancelled token throws
/// <see cref="OperationCanceledException"/> (well within a second on
/// ordinary hardware), and a limit that would be passed throws
/// <see cref="PdfLimitExceededException"/>.
/// </para>
/// <para>
/// <c>PageRasterizer</c> and <c>TextExtractor</c> open a scope per call; open
/// one yourself to cancel or limit other work:
/// <code>
/// using (PdfWorkScope.Enter(limits, token))
/// {
///     string svg = new SvgRenderer().RenderPage(doc, 0);
/// }
/// </code>
/// Scopes nest: work inside an inner scope also honours every enclosing
/// scope's token and limits. The scope flows with the execution context
/// (into awaited code and <c>Task.Run</c>), and is restored when disposed.
/// Dispose scopes in the reverse order of entering them; a scope disposed
/// early (out of order) simply stops applying.
/// </para>
/// </remarks>
public sealed class PdfWorkScope : IDisposable
{
    private static readonly AsyncLocal<PdfWorkScope?> CurrentScope = new AsyncLocal<PdfWorkScope?>();

    private readonly PdfWorkScope? _parent;
    private long _memoryBytes;
    private long _operators;
    private bool _disposed;

    private PdfWorkScope(PdfLimits limits, CancellationToken cancellationToken, PdfWorkScope? parent)
    {
        Limits = limits;
        CancellationToken = cancellationToken;
        _parent = parent;
    }

    /// <summary>Gets the innermost open scope on the current flow, or null when none is open.</summary>
    public static PdfWorkScope? Current => Live(CurrentScope.Value);

    /// <summary>Gets this scope's limits.</summary>
    public PdfLimits Limits { get; }

    /// <summary>Gets this scope's cancellation token.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the large-buffer bytes charged to this scope so far.</summary>
    public long MemoryBytes => Interlocked.Read(ref _memoryBytes);

    /// <summary>Gets the content-stream operators counted in this scope so far.</summary>
    public long ContentOperators => Interlocked.Read(ref _operators);

    /// <summary>
    /// Opens a scope on the current flow. Dispose it (for example with
    /// <c>using</c>) to restore the previous scope.
    /// </summary>
    /// <param name="limits">The limits to apply; null for none.</param>
    /// <param name="cancellationToken">The token to honour.</param>
    /// <returns>The new scope.</returns>
    public static PdfWorkScope Enter(PdfLimits? limits, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PdfWorkScope scope = new PdfWorkScope(limits ?? new PdfLimits(), cancellationToken, Live(CurrentScope.Value));
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Throws <see cref="OperationCanceledException"/> when the token of the
    /// current scope or of any enclosing scope is cancelled. Does nothing
    /// outside a scope.
    /// </summary>
    public static void ThrowIfCancellationRequested()
    {
        for (PdfWorkScope? s = Live(CurrentScope.Value); s is not null; s = Live(s._parent))
        {
            s.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Counts content-stream operators against
    /// <see cref="PdfLimits.MaxContentOperators"/> and checks cancellation.
    /// </summary>
    /// <param name="count">The number of operators processed.</param>
    public static void CountOperators(long count = 1)
    {
        for (PdfWorkScope? s = Live(CurrentScope.Value); s is not null; s = Live(s._parent))
        {
            s.CancellationToken.ThrowIfCancellationRequested();
            long total = Interlocked.Add(ref s._operators, count);
            if (s.Limits.MaxContentOperators is long max && total > max)
            {
                throw new PdfLimitExceededException(nameof(PdfLimits.MaxContentOperators), max, total);
            }
        }
    }

    /// <summary>
    /// Charges a large buffer against <see cref="PdfLimits.MaxMemoryBytes"/>
    /// and checks cancellation. Call before allocating it.
    /// </summary>
    /// <param name="bytes">The buffer size in bytes.</param>
    public static void ChargeMemory(long bytes)
    {
        for (PdfWorkScope? s = Live(CurrentScope.Value); s is not null; s = Live(s._parent))
        {
            s.CancellationToken.ThrowIfCancellationRequested();
            long total = Interlocked.Add(ref s._memoryBytes, Math.Max(0, bytes));
            if (s.Limits.MaxMemoryBytes is long max && total > max)
            {
                throw new PdfLimitExceededException(nameof(PdfLimits.MaxMemoryBytes), max, total);
            }
        }
    }

    /// <summary>
    /// Checks a stream's decoded size so far against
    /// <see cref="PdfLimits.MaxDecodedStreamBytes"/> and checks cancellation.
    /// Decoders call this while they produce output.
    /// </summary>
    /// <param name="bytes">The decoded size so far, in bytes.</param>
    public static void CheckDecodedSize(long bytes)
    {
        for (PdfWorkScope? s = Live(CurrentScope.Value); s is not null; s = Live(s._parent))
        {
            s.CancellationToken.ThrowIfCancellationRequested();
            if (s.Limits.MaxDecodedStreamBytes is long max && bytes > max)
            {
                throw new PdfLimitExceededException(nameof(PdfLimits.MaxDecodedStreamBytes), max, bytes);
            }
        }
    }

    /// <summary>
    /// Checks an output raster's size against
    /// <see cref="PdfLimits.MaxOutputPixels"/>, then charges its bytes
    /// (four per pixel) as memory. Call before allocating it.
    /// </summary>
    /// <param name="width">The raster width in pixels.</param>
    /// <param name="height">The raster height in pixels.</param>
    public static void ChargeRaster(long width, long height)
    {
        CheckOutputPixels(width, height);
        ChargeMemory(Math.Max(0, width) * Math.Max(0, height) * 4);
    }

    /// <summary>
    /// Checks an output raster's size against
    /// <see cref="PdfLimits.MaxOutputPixels"/> without charging memory.
    /// </summary>
    /// <param name="width">The raster width in pixels.</param>
    /// <param name="height">The raster height in pixels.</param>
    public static void CheckOutputPixels(long width, long height)
    {
        long pixels = Math.Max(0, width) * Math.Max(0, height);
        for (PdfWorkScope? s = Live(CurrentScope.Value); s is not null; s = Live(s._parent))
        {
            if (s.Limits.MaxOutputPixels is long max && pixels > max)
            {
                throw new PdfLimitExceededException(nameof(PdfLimits.MaxOutputPixels), max, pixels);
            }
        }
    }

    /// <summary>
    /// Returns whether <paramref name="exception"/> ends the operation (a
    /// cancellation or an exceeded limit) and must not be absorbed by code
    /// that skips a damaged object and carries on.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>True for <see cref="OperationCanceledException"/> and <see cref="PdfLimitExceededException"/>.</returns>
    public static bool IsAbort(Exception exception) =>
        exception is OperationCanceledException or PdfLimitExceededException;

    /// <summary>Restores the scope that was current when this one was entered.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            // Scopes disposed out of order are skipped: the nearest one still
            // open becomes current.
            CurrentScope.Value = Live(_parent);
        }
    }

    // The scope itself, or its nearest ancestor that is not disposed.
    private static PdfWorkScope? Live(PdfWorkScope? scope)
    {
        while (scope is not null && scope._disposed)
        {
            scope = scope._parent;
        }

        return scope;
    }
}
