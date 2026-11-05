// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — JPEG facts a printer needs to pass a JPEG through (Print item f).
// SPEC:  ITU-T T.81 (JPEG) frame headers; the Adobe APP14 marker (Adobe
//        Technical Note 5116) records the colour transform.

namespace Chuvadi.Pdf.Rendering.DisplayList;

internal static class JpegInfo
{
    // The APP14 "Adobe" transform byte, or the JFIF default.
    internal static JpegColorTransform ReadTransform(byte[] jpeg)
    {
        int components = 0;
        int? transform = null;
        Walk(jpeg, (marker, offset, length) =>
        {
            if (marker == 0xEE && length >= 12 && jpeg[offset] == 'A' && jpeg[offset + 1] == 'd' && jpeg[offset + 2] == 'o'
                && jpeg[offset + 3] == 'b' && jpeg[offset + 4] == 'e')
            {
                transform = jpeg[offset + 11];
            }
            else if (IsFrame(marker) && length >= 6)
            {
                components = jpeg[offset + 5];
            }
        });
        return transform switch
        {
            1 => JpegColorTransform.YCbCr,
            2 => JpegColorTransform.Ycck,
            0 => JpegColorTransform.None,
            _ => components == 3 ? JpegColorTransform.YCbCr : JpegColorTransform.None,
        };
    }

    internal static int ReadComponents(byte[] jpeg)
    {
        int components = 0;
        Walk(jpeg, (marker, offset, length) =>
        {
            if (components == 0 && IsFrame(marker) && length >= 6)
            {
                components = jpeg[offset + 5];
            }
        });
        return components;
    }

    private static bool IsFrame(int marker) => marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    // Calls back with (marker, payload offset, payload length) for each
    // marker segment before the scan data.
    private static void Walk(byte[] jpeg, System.Action<int, int, int> segment)
    {
        int i = 2;
        while (i + 4 <= jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                return;
            }

            int marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2;
                continue;
            }

            int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (length < 2 || i + 2 + length > jpeg.Length)
            {
                return;
            }

            segment(marker, i + 4, length - 2);
            if (marker == 0xDA)
            {
                return;
            }

            i += 2 + length;
        }
    }
}
