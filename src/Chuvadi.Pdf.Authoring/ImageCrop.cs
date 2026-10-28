// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The part of an image to show, in source pixels measured from the image's
/// top-left corner. The rectangle may extend past the image (the area
/// outside shows nothing), as cropping in Word and Excel allows.
/// </summary>
/// <param name="X">Left edge, in source pixels.</param>
/// <param name="Y">Top edge, in source pixels.</param>
/// <param name="Width">Width, in source pixels; must be positive.</param>
/// <param name="Height">Height, in source pixels; must be positive.</param>
public readonly record struct ImageCrop(double X, double Y, double Width, double Height);
