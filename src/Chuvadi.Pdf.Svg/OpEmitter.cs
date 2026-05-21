// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C SVG 1.1 §8 (Paths), §9.6 (image), §14 (clipping), §7.6 (transform)
// PHASE: v2.0.0 R2 — SVG renderer

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Rendering.DisplayList;
using Path = Chuvadi.Pdf.Graphics.Path;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Walks a <see cref="PageDisplayList"/> and emits SVG markup using a
/// shared <see cref="SvgWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two passes:
/// </para>
/// <list type="number">
///   <item>
///     <c>Discover</c>: counts glyph occurrences, registers clip paths,
///     and collects the first-sight ordering of distinct glyph data
///     strings so that ids allocated between passes are deterministic.
///   </item>
///   <item>
///     <c>Emit</c>: writes the SVG. Clipping is realised by wrapping the
///     active ops in nested <c>&lt;g clip-path="url(#…)"&gt;</c> groups;
///     identical clip paths share a single <c>&lt;clipPath&gt;</c>
///     definition. Glyphs that occur twice or more are emitted as
///     <c>&lt;use href="#…"&gt;</c> referencing a shared
///     <c>&lt;path&gt;</c> in defs.
///   </item>
/// </list>
/// </remarks>
internal sealed class OpEmitter
{
    private readonly SvgWriter _writer;
    private readonly SvgRenderOptions _options;
    private readonly ClipManager _clips;
    private readonly GlyphCache _glyphs;
    private readonly List<string> _glyphFirstSightOrder;
    private readonly HashSet<string> _glyphSeen;

    // Clip wrappers currently open in the emit pass; identity tracked by
    // the ClipPath.Path reference so we know when nested groups can close.
    private readonly List<Path> _openClipStack;

    internal OpEmitter(SvgWriter writer, SvgRenderOptions options)
    {
        _writer = writer;
        _options = options;
        _clips = new ClipManager();
        _glyphs = new GlyphCache();
        _glyphFirstSightOrder = new List<string>();
        _glyphSeen = new HashSet<string>(StringComparer.Ordinal);
        _openClipStack = new List<Path>();
    }

    /// <summary>
    /// Pass 1: walk the list to discover repeated glyphs. Walks nested
    /// display lists too so glyph counts span Form XObject boundaries.
    /// </summary>
    internal void Discover(PageDisplayList list)
    {
        foreach (RenderOp op in list.Ops)
        {
            if (op is DrawGlyphOp glyph)
            {
                string data = PathSerializer.Serialise(glyph.Path, _writer);
                _glyphs.Observe(data);

                if (_glyphSeen.Add(data))
                {
                    _glyphFirstSightOrder.Add(data);
                }
            }
            else if (op is NestedDisplayListOp nested)
            {
                Discover(nested.Inner);
            }
        }
    }

    /// <summary>
    /// Returns the defs entries (clip definitions + repeated glyphs) ready
    /// to write into a <c>&lt;defs&gt;</c> block. Must be called between
    /// <see cref="Discover"/> and <see cref="Emit"/>.
    /// </summary>
    internal IReadOnlyList<GlyphCache.DefsEntry> AllocateGlyphIds()
    {
        return _glyphs.AllocateIds(_glyphFirstSightOrder, _writer);
    }

    /// <summary>
    /// Writes the <c>&lt;defs&gt;</c> block with all collected glyphs and
    /// clip paths. Emits an empty element when nothing to define.
    /// </summary>
    internal void WriteDefs(IReadOnlyList<GlyphCache.DefsEntry> glyphDefs)
    {
        // Walk once to also collect clip definitions; emitting defs early
        // means we must walk before the main paint pass for clip discovery.
        // We do this here to keep emit-pass clipping ergonomic.
        // First: register every clip used in the list.
        // The Emit pass below also calls GetOrAllocateId, so any clip not
        // registered here will simply be registered there on first use —
        // but then its <clipPath> definition would appear AFTER the use,
        // which most SVG parsers tolerate but is ugly. Pre-register here.
        // (We rely on the same pass already done by Discover for glyphs
        // to keep the second walk cheap.)

        bool hasGlyphDefs = glyphDefs.Count > 0;
        bool hasClipDefs = _clips.Entries.Count > 0;

        if (!hasGlyphDefs && !hasClipDefs)
        {
            return;
        }

        _writer.OpenTag("defs");
        _writer.CloseStartTag();

        for (int i = 0; i < glyphDefs.Count; i++)
        {
            GlyphCache.DefsEntry entry = glyphDefs[i];
            _writer.OpenTag("path");
            _writer.Attr("id", entry.Id);
            _writer.AttrLiteral("d", entry.PathData);
            _writer.SelfCloseTag();
        }

        for (int i = 0; i < _clips.Entries.Count; i++)
        {
            ClipManager.ClipEntry entry = _clips.Entries[i];
            _writer.OpenTag("clipPath");
            _writer.Attr("id", entry.Id);
            _writer.AttrLiteral("clipPathUnits", "userSpaceOnUse");
            _writer.CloseStartTag();

            _writer.OpenTag("path");
            _writer.AttrLiteral("d", PathSerializer.Serialise(entry.Clip.Path, _writer));

            if (entry.Clip.Rule == FillRule.EvenOdd)
            {
                _writer.AttrLiteral("clip-rule", "evenodd");
            }

            _writer.SelfCloseTag();
            _writer.CloseTag("clipPath");
        }

        _writer.CloseTag("defs");
    }

    /// <summary>
    /// Pass 2: emit the painted content. Caller is responsible for opening
    /// any wrapping <c>&lt;g transform="…"&gt;</c> for page Y-flip before
    /// calling this method.
    /// </summary>
    internal void Emit(PageDisplayList list)
    {
        foreach (RenderOp op in list.Ops)
        {
            AdjustClipStack(op.Clips);
            EmitOp(op);
        }

        // Close any clip groups still open at end of list.
        AdjustClipStack(Array.Empty<ClipPath>());
    }

    private void AdjustClipStack(IReadOnlyList<ClipPath> targetClips)
    {
        // Compute the longest common prefix between the currently open
        // stack and the target. Close anything past that; then open the
        // remainder. Identity is on ClipPath.Path reference (BuilderGraphicsState
        // shares the same Path reference across ops while a clip is active;
        // ApplyDeferredClip pushes a fresh ClipPath each time).
        int common = 0;
        int maxCommon = Math.Min(_openClipStack.Count, targetClips.Count);

        while (common < maxCommon &&
               ReferenceEquals(_openClipStack[common], targetClips[common].Path))
        {
            common++;
        }

        // Close from top down to the common boundary.
        while (_openClipStack.Count > common)
        {
            _writer.CloseTag("g");
            _openClipStack.RemoveAt(_openClipStack.Count - 1);
        }

        // Open new groups from common to end of target.
        for (int i = common; i < targetClips.Count; i++)
        {
            ClipPath clip = targetClips[i];
            string id = _clips.GetOrAllocateId(clip, _writer);
            _writer.OpenTag("g");
            _writer.AttrLiteral("clip-path", "url(#" + id + ")");
            _writer.CloseStartTag();
            _openClipStack.Add(clip.Path);
        }
    }

    private void EmitOp(RenderOp op)
    {
        switch (op)
        {
            case FillPathOp fp:
                EmitFill(fp);
                break;
            case StrokePathOp sp:
                EmitStroke(sp);
                break;
            case DrawGlyphOp gp:
                EmitGlyph(gp);
                break;
            case DrawImageOp ip:
                EmitImage(ip);
                break;
            case NestedDisplayListOp np:
                EmitNested(np);
                break;
        }
    }

    private void EmitFill(FillPathOp op)
    {
        string d = PathSerializer.Serialise(op.Path, _writer);
        string color = ColorFormatter.ToSvgColor(op.Color);
        double alpha = ColorFormatter.Alpha(op.Color);

        _writer.OpenTag("path");
        _writer.AttrLiteral("d", d);
        _writer.AttrLiteral("fill", color);

        if (op.Rule == FillRule.EvenOdd)
        {
            _writer.AttrLiteral("fill-rule", "evenodd");
        }

        if (alpha < 1.0)
        {
            _writer.AttrDouble("fill-opacity", alpha);
        }

        _writer.SelfCloseTag();
    }

    private void EmitStroke(StrokePathOp op)
    {
        string d = PathSerializer.Serialise(op.Path, _writer);
        string color = ColorFormatter.ToSvgColor(op.Style.Color);
        double alpha = ColorFormatter.Alpha(op.Style.Color);

        _writer.OpenTag("path");
        _writer.AttrLiteral("d", d);
        _writer.AttrLiteral("fill", "none");
        _writer.AttrLiteral("stroke", color);
        _writer.AttrDouble("stroke-width", op.Style.Width);

        if (op.Style.Cap != LineCap.Butt)
        {
            _writer.AttrLiteral(
                "stroke-linecap",
                op.Style.Cap == LineCap.Round ? "round" : "square");
        }

        if (op.Style.Join != LineJoin.Miter)
        {
            _writer.AttrLiteral(
                "stroke-linejoin",
                op.Style.Join == LineJoin.Round ? "round" : "bevel");
        }

        if (op.Style.Join == LineJoin.Miter && op.Style.MiterLimit != 4.0)
        {
            _writer.AttrDouble("stroke-miterlimit", op.Style.MiterLimit);
        }

        if (op.Style.DashPattern.Length > 0)
        {
            StringBuilder dash = new StringBuilder();

            for (int i = 0; i < op.Style.DashPattern.Length; i++)
            {
                if (i > 0)
                {
                    dash.Append(' ');
                }

                _writer.AppendPathNumber(dash, op.Style.DashPattern[i], needsLeadingSpace: false);
            }

            _writer.AttrLiteral("stroke-dasharray", dash.ToString());

            if (op.Style.DashOffset != 0.0)
            {
                _writer.AttrDouble("stroke-dashoffset", op.Style.DashOffset);
            }
        }

        if (alpha < 1.0)
        {
            _writer.AttrDouble("stroke-opacity", alpha);
        }

        _writer.SelfCloseTag();
    }

    private void EmitGlyph(DrawGlyphOp op)
    {
        string d = PathSerializer.Serialise(op.Path, _writer);
        string color = ColorFormatter.ToSvgColor(op.Color);
        double alpha = ColorFormatter.Alpha(op.Color);

        string? defsId = _glyphs.GetDefsId(d);

        if (defsId is null)
        {
            // Singleton: emit inline.
            _writer.OpenTag("path");
            _writer.AttrLiteral("d", d);
            _writer.AttrLiteral("fill", color);

            if (alpha < 1.0)
            {
                _writer.AttrDouble("fill-opacity", alpha);
            }

            _writer.SelfCloseTag();
        }
        else
        {
            // Dedup hit: emit <use>.
            _writer.OpenTag("use");
            _writer.AttrLiteral("href", "#" + defsId);
            _writer.AttrLiteral("fill", color);

            if (alpha < 1.0)
            {
                _writer.AttrDouble("fill-opacity", alpha);
            }

            _writer.SelfCloseTag();
        }
    }

    private void EmitImage(DrawImageOp op)
    {
        ImageFrame frame = op.Image;
        string dataUri;

        using (MemoryStream ms = new MemoryStream())
        {
            // PNG with alpha if the original source had alpha; RGB-only
            // is sufficient for opaque JPEG-sourced images.
            bool includeAlpha = frame.OriginalFormat == ImageColorFormat.Rgba32;
            PngEncoder.Encode(frame, ms, includeAlpha);
            dataUri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }

        // The Do operator in PDF places the image such that the unit
        // square (0,0)–(1,1) in image space maps to op.DeviceTransform. In
        // SVG we want the image to occupy a 1×1 box and let the transform
        // place it. The PDF image space has Y up; SVG <image> has Y down;
        // we flip via a local matrix translate(0,1) scale(1,-1) baked in
        // before the device transform.
        Transform t = op.DeviceTransform;

        _writer.OpenTag("image");
        _writer.AttrLiteral("width", "1");
        _writer.AttrLiteral("height", "1");
        _writer.AttrLiteral("preserveAspectRatio", "none");

        StringBuilder tx = new StringBuilder(64);
        tx.Append("matrix(");
        _writer.AppendPathNumber(tx, t.A, false);
        _writer.AppendPathNumber(tx, t.B, true);
        _writer.AppendPathNumber(tx, -t.C, true);
        _writer.AppendPathNumber(tx, -t.D, true);
        _writer.AppendPathNumber(tx, t.E + t.C, true);
        _writer.AppendPathNumber(tx, t.F + t.D, true);
        tx.Append(')');
        _writer.AttrLiteral("transform", tx.ToString());
        _writer.AttrLiteral("href", dataUri);
        _writer.SelfCloseTag();
    }

    private void EmitNested(NestedDisplayListOp op)
    {
        Transform t = op.CtmComposition;
        bool identity = t.IsIdentity;

        if (!identity)
        {
            StringBuilder tx = new StringBuilder(64);
            tx.Append("matrix(");
            _writer.AppendPathNumber(tx, t.A, false);
            _writer.AppendPathNumber(tx, t.B, true);
            _writer.AppendPathNumber(tx, t.C, true);
            _writer.AppendPathNumber(tx, t.D, true);
            _writer.AppendPathNumber(tx, t.E, true);
            _writer.AppendPathNumber(tx, t.F, true);
            tx.Append(')');

            _writer.OpenTag("g");
            _writer.AttrLiteral("transform", tx.ToString());
            _writer.CloseStartTag();
        }

        // The nested list's clips form a fresh stack — save and restore
        // the outer stack across the recursion.
        List<Path> savedClips = new List<Path>(_openClipStack);
        _openClipStack.Clear();

        Emit(op.Inner);

        // Restore outer stack — Emit() will have closed all of the nested
        // ones in its tail AdjustClipStack call.
        _openClipStack.AddRange(savedClips);

        if (!identity)
        {
            _writer.CloseTag("g");
        }
    }

    /// <summary>
    /// Pre-walks the list to register every clip path with the
    /// <see cref="ClipManager"/> so that <c>&lt;clipPath&gt;</c> defs are
    /// emitted before any <c>&lt;g clip-path&gt;</c> reference. Called
    /// once between <see cref="Discover"/> and <see cref="WriteDefs"/>.
    /// </summary>
    internal void RegisterClips(PageDisplayList list)
    {
        foreach (RenderOp op in list.Ops)
        {
            IReadOnlyList<ClipPath> clips = op.Clips;

            for (int i = 0; i < clips.Count; i++)
            {
                _clips.GetOrAllocateId(clips[i], _writer);
            }

            if (op is NestedDisplayListOp nested)
            {
                RegisterClips(nested.Inner);
            }
        }
    }
}
