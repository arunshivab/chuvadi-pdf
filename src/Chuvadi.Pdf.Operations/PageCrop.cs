// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 (page boundaries); §8.5.4 (clipping)

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Identifies a single page to crop and the crop rectangle, in PDF user-space
/// points (origin at the bottom-left of the page), to confine it to.
/// </summary>
public readonly struct PageCrop : IEquatable<PageCrop>
{
    /// <summary>Initializes a new <see cref="PageCrop"/> with <see cref="PageCropMode.ClipOnly"/>.</summary>
    /// <param name="pageIndex">The zero-based index of the page to crop.</param>
    /// <param name="cropBox">The crop rectangle in PDF user-space points.</param>
    public PageCrop(int pageIndex, RectangleF cropBox)
        : this(pageIndex, cropBox, PageCropMode.ClipOnly)
    {
    }

    /// <summary>Initializes a new <see cref="PageCrop"/>.</summary>
    /// <param name="pageIndex">The zero-based index of the page to crop.</param>
    /// <param name="cropBox">The crop rectangle in PDF user-space points.</param>
    /// <param name="mode">How the page is confined to the crop rectangle.</param>
    public PageCrop(int pageIndex, RectangleF cropBox, PageCropMode mode)
    {
        PageIndex = pageIndex;
        CropBox = cropBox;
        Mode = mode;
    }

    /// <summary>Gets the zero-based index of the page to crop.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the crop rectangle in PDF user-space points.</summary>
    public RectangleF CropBox { get; }

    /// <summary>Gets how the page is confined to the crop rectangle.</summary>
    public PageCropMode Mode { get; }

    /// <summary>Determines whether this value equals <paramref name="other"/>.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when both values are equal.</returns>
    public bool Equals(PageCrop other) => PageIndex == other.PageIndex && CropBox.Equals(other.CropBox) && Mode == other.Mode;

    /// <summary>Determines whether this value equals <paramref name="obj"/>.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal <see cref="PageCrop"/>.</returns>
    public override bool Equals(object? obj) => obj is PageCrop other && Equals(other);

    /// <summary>Returns a hash code for this value.</summary>
    /// <returns>A hash code combining the page index and crop rectangle.</returns>
    public override int GetHashCode() => HashCode.Combine(PageIndex, CropBox, Mode);

    /// <summary>Determines whether two <see cref="PageCrop"/> values are equal.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(PageCrop left, PageCrop right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="PageCrop"/> values are not equal.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values are not equal.</returns>
    public static bool operator !=(PageCrop left, PageCrop right) => !left.Equals(right);
}
