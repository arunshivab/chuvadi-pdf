// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — WPF rendering adapter

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using PathSegment = Chuvadi.Pdf.Rendering.DisplayList.PathSegment;
using FillRule = Chuvadi.Pdf.Rendering.DisplayList.FillRule;

namespace Chuvadi.Pdf.Rendering.Wpf;

/// <summary>
/// Renders a <see cref="PageDisplayList"/> into a WPF <see cref="DrawingVisual"/>.
/// </summary>
/// <remarks>
/// <para>
/// Translates each <see cref="RenderOp"/> into WPF drawing primitives via
/// <see cref="DrawingContext"/>. <see cref="PathOp"/> becomes a
/// <see cref="StreamGeometry"/> drawn with <see cref="DrawingContext.DrawGeometry"/>;
/// <see cref="TextOp"/> becomes a <see cref="FormattedText"/> drawn with
/// <see cref="DrawingContext.DrawText"/>; <see cref="ImageOp"/> becomes a
/// <see cref="BitmapSource"/> drawn with <see cref="DrawingContext"/>.
/// </para>
/// <para>
/// Coordinate handling: the renderer applies an outer
/// <c>(1, 0, 0, -1, 0, pageHeight)</c> transform so PDF coordinates flow
/// through directly. Text runs receive a local counter-flip so glyphs read
/// upright.
/// </para>
/// </remarks>
public sealed class WpfRenderer
{
    /// <summary>Renders a page to a <see cref="DrawingVisual"/>.</summary>
    public DrawingVisual RenderPage(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        PageDisplayList list = DisplayListBuilder.Build(document, pageIndex);
        return Render(list);
    }

    /// <summary>Renders a pre-built display list to a <see cref="DrawingVisual"/>.</summary>
    public DrawingVisual Render(PageDisplayList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        DrawingVisual visual = new();
        using DrawingContext dc = visual.RenderOpen();

        // Page flip
        MatrixTransform pageFlip = new(new Matrix(1, 0, 0, -1, 0, list.MediaHeight));
        dc.PushTransform(pageFlip);

        Stack<int> pushDepth = new();
        int currentDepth = 1;   // for the pageFlip we just pushed

        foreach (RenderOp op in list)
        {
            switch (op)
            {
                case PathOp p: DrawPath(dc, p); break;
                case TextOp t: DrawText(dc, t); break;
                case ImageOp i: DrawImage(dc, i); break;
                case TransformOp xf:
                    if (xf.Push) { dc.PushTransform(Transform.Identity); pushDepth.Push(currentDepth); currentDepth++; }
                    else if (currentDepth > 1) { dc.Pop(); currentDepth--; }
                    break;
                case OpacityOp op2:
                    if (op2.Push) { dc.PushOpacity(op2.Alpha); currentDepth++; }
                    else if (currentDepth > 1) { dc.Pop(); currentDepth--; }
                    break;
                case ClipOp:
                    // Not implemented in v1 — clipping mid-frame requires Geometry-based
                    // push which interacts with the transform stack. Phase 2.2.
                    break;
                case BlendModeOp:
                    // WPF doesn't expose PDF blend modes directly. Phase 2.2 will
                    // approximate the most common ones (Multiply/Screen).
                    break;
                default: break;
            }
        }
        while (currentDepth > 0) { dc.Pop(); currentDepth--; }
        return visual;
    }

    private static void DrawPath(DrawingContext dc, PathOp op)
    {
        StreamGeometry sg = new();
        using (StreamGeometryContext ctx = sg.Open())
        {
            bool started = false;
            foreach (PathSegment seg in op.Geometry.Segments)
            {
                switch (seg.Command)
                {
                    case PathCommand.MoveTo:
                        ctx.BeginFigure(new Point(seg.X1, seg.Y1), isFilled: true, isClosed: false);
                        started = true;
                        break;
                    case PathCommand.LineTo:
                        if (started) { ctx.LineTo(new Point(seg.X1, seg.Y1), isStroked: true, isSmoothJoin: false); }
                        break;
                    case PathCommand.CubicTo:
                        if (started)
                        {
                            ctx.BezierTo(new Point(seg.X1, seg.Y1),
                                new Point(seg.X2, seg.Y2),
                                new Point(seg.X3, seg.Y3),
                                isStroked: true, isSmoothJoin: false);
                        }
                        break;
                    case PathCommand.Close:
                        // StreamGeometry closes via the isClosed flag at BeginFigure;
                        // there's no mid-path close. WPF auto-closes when next BeginFigure
                        // or end-of-figure happens. For our purposes we accept the slight
                        // visual difference.
                        break;
                    default: break;
                }
            }
        }
        sg.FillRule = op.FillRule == FillRule.EvenOdd ? System.Windows.Media.FillRule.EvenOdd : System.Windows.Media.FillRule.Nonzero;
        Brush? fill = op.Mode is PaintMode.Fill or PaintMode.FillAndStroke
            ? new SolidColorBrush(WpfColor(op.FillColor)) : null;
        Pen? pen = op.Mode is PaintMode.Stroke or PaintMode.FillAndStroke
            ? new Pen(new SolidColorBrush(WpfColor(op.StrokeColor)), op.Stroke?.LineWidth ?? 1) : null;
        dc.DrawGeometry(fill, pen, sg);
    }

    private static void DrawText(DrawingContext dc, TextOp op)
    {
        if (op.Glyphs.Count == 0) { return; }
        System.Text.StringBuilder sb = new();
        foreach (DisplayListGlyph g in op.Glyphs) { sb.Append(g.Unicode); }
        string text = sb.ToString();

        Typeface tf = new(op.BaseFont);
        FormattedText ft = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            tf,
            op.FontSize,
            new SolidColorBrush(WpfColor(op.FillColor)),
            1.0);

        // Compose the outer page-flip + run transform + local counter-flip.
        AffineMatrix localFlip = new(1, 0, 0, -1, 0, 0);
        AffineMatrix combined = localFlip.Multiply(op.Transform);
        MatrixTransform xf = new(new Matrix(combined.A, combined.B, combined.C, combined.D, combined.E, combined.F));
        dc.PushTransform(xf);
        dc.DrawText(ft, new Point(0, 0));
        dc.Pop();
    }

    private static void DrawImage(DrawingContext dc, ImageOp op)
    {
        BitmapSource? bitmap = TryDecodeBitmap(op);
        if (bitmap is null) { return; }
        MatrixTransform xf = new(new Matrix(op.Transform.A, op.Transform.B,
            op.Transform.C, op.Transform.D, op.Transform.E, op.Transform.F));
        dc.PushTransform(xf);
        dc.DrawImage(bitmap, new System.Windows.Rect(0, 0, 1, 1));
        dc.Pop();
    }

    private static BitmapSource? TryDecodeBitmap(ImageOp op)
    {
        // JPEG passthrough via WPF's BitmapDecoder.
        if (op.Format == ImageFormat.Jpeg)
        {
            using MemoryStream ms = new(op.PixelData);
            BitmapImage img = new();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
        // Raw RGB / Gray.
        if (op.Format == ImageFormat.Raw && op.BitsPerComponent == 8)
        {
            PixelFormat fmt = op.ColorSpace switch
            {
                PdfColorSpace.DeviceGray => PixelFormats.Gray8,
                PdfColorSpace.DeviceRgb => PixelFormats.Rgb24,
                _ => PixelFormats.Rgb24,
            };
            int channels = op.ColorSpace switch
            {
                PdfColorSpace.DeviceGray => 1,
                PdfColorSpace.DeviceRgb => 3,
                PdfColorSpace.DeviceCmyk => 3, // converted below
                _ => 3,
            };
            byte[] pixels = op.PixelData;
            if (op.ColorSpace == PdfColorSpace.DeviceCmyk)
            {
                pixels = CmykToRgb(op.PixelData, op.Width, op.Height);
            }
            int stride = op.Width * channels;
            return BitmapSource.Create(op.Width, op.Height, 96, 96, fmt, null, pixels, stride);
        }
        return null;
    }

    private static byte[] CmykToRgb(byte[] cmyk, int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        int n = width * height;
        for (int i = 0; i < n; i++)
        {
            double c = cmyk[i * 4] / 255.0;
            double m = cmyk[i * 4 + 1] / 255.0;
            double y = cmyk[i * 4 + 2] / 255.0;
            double k = cmyk[i * 4 + 3] / 255.0;
            rgb[i * 3] = (byte)((1 - c) * (1 - k) * 255);
            rgb[i * 3 + 1] = (byte)((1 - m) * (1 - k) * 255);
            rgb[i * 3 + 2] = (byte)((1 - y) * (1 - k) * 255);
        }
        return rgb;
    }

    private static Color WpfColor(PdfColor c)
    {
        (double r, double g, double b) = c.ToSrgb();
        return Color.FromRgb(
            (byte)Math.Clamp(r * 255, 0, 255),
            (byte)Math.Clamp(g * 255, 0, 255),
            (byte)Math.Clamp(b * 255, 0, 255));
    }
}
