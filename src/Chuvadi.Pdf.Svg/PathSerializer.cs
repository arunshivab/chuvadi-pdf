// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C SVG 1.1 §8.3 — Path data syntax
// PHASE: v2.0.0 R2 — SVG renderer

using System.Text;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Serialises a <see cref="Path"/> from <c>Chuvadi.Pdf.Graphics</c> into the
/// SVG <c>d</c> attribute syntax.
/// </summary>
/// <remarks>
/// <para>
/// PDF path segments map cleanly to SVG path commands:
/// </para>
/// <list type="bullet">
///   <item><see cref="PathSegmentKind.MoveTo"/> → <c>M x y</c></item>
///   <item><see cref="PathSegmentKind.LineTo"/> → <c>L x y</c></item>
///   <item><see cref="PathSegmentKind.CubicBezierTo"/> → <c>C cx1 cy1 cx2 cy2 ex ey</c></item>
///   <item><see cref="PathSegmentKind.ClosePath"/> → <c>Z</c></item>
/// </list>
/// <para>
/// Y coordinates from PDF user space (Y up, origin bottom-left) are
/// negated; the renderer wraps the entire page in a transform that flips
/// the Y axis once at the top level, so emitting PDF-native coordinates
/// here is correct.
/// </para>
/// </remarks>
internal static class PathSerializer
{
    internal static string Serialise(Path path, SvgWriter writer)
    {
        StringBuilder sb = new StringBuilder(path.Count * 16);
        bool first = true;

        foreach (PathSegment seg in path.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    if (!first)
                    {
                        sb.Append(' ');
                    }
                    sb.Append('M');
                    writer.AppendPathNumber(sb, seg.P0.X, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P0.Y, needsLeadingSpace: true);
                    break;

                case PathSegmentKind.LineTo:
                    if (!first)
                    {
                        sb.Append(' ');
                    }
                    sb.Append('L');
                    writer.AppendPathNumber(sb, seg.P0.X, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P0.Y, needsLeadingSpace: true);
                    break;

                case PathSegmentKind.CubicBezierTo:
                    if (!first)
                    {
                        sb.Append(' ');
                    }
                    sb.Append('C');
                    writer.AppendPathNumber(sb, seg.P0.X, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P0.Y, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P1.X, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P1.Y, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P2.X, needsLeadingSpace: true);
                    writer.AppendPathNumber(sb, seg.P2.Y, needsLeadingSpace: true);
                    break;

                case PathSegmentKind.ClosePath:
                    if (!first)
                    {
                        sb.Append(' ');
                    }
                    sb.Append('Z');
                    break;
            }

            first = false;
        }

        return sb.ToString();
    }
}
