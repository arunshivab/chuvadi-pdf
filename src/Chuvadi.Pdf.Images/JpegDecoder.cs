// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// ISO/IEC 10918-1 JPEG decoder: baseline (SOF0) and progressive (SOF2) DCT,
// 8-bit, 1/3/4 components (grayscale, YCbCr/RGB, CMYK/YCCK with Adobe APP14).

using System;
using System.IO;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Decodes baseline sequential (SOF0) and progressive (SOF2) DCT JPEG images
/// into an <see cref="ImageFrame"/>. Supports 8-bit precision with 1 component
/// (grayscale), 3 components (YCbCr or RGB), and 4 components (CMYK or YCCK,
/// using the Adobe APP14 colour transform). Chroma subsampling and restart
/// intervals are handled.
/// </summary>
/// <remarks>
/// Not supported: 12-bit precision, arithmetic coding (SOF9–SOF11), and
/// lossless modes. CMYK/YCCK output is converted to RGB for display, honouring
/// the Adobe inverted-channel convention.
/// </remarks>
public static class JpegDecoder
{
    /// <summary>Decodes a JPEG from a byte array.</summary>
    /// <exception cref="ArgumentNullException">When <paramref name="data"/> is null.</exception>
    /// <exception cref="ImageException">When the JPEG is invalid or unsupported.</exception>
    public static ImageFrame Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        JpegContext ctx = new JpegContext(data);
        ctx.Parse();
        return ctx.BuildFrame();
    }

    /// <summary>Decodes a JPEG from a stream.</summary>
    /// <exception cref="ArgumentNullException">When <paramref name="input"/> is null.</exception>
    /// <exception cref="ImageException">When the JPEG is invalid or unsupported.</exception>
    public static ImageFrame Decode(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using MemoryStream ms = new MemoryStream();
        input.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    // ── Marker constants ──────────────────────────────────────────────────
    private const byte MarkerPrefix = 0xFF;
    private const byte SOI = 0xD8;
    private const byte EOI = 0xD9;
    private const byte SOF0 = 0xC0; // baseline DCT
    private const byte SOF1 = 0xC1; // extended sequential DCT
    private const byte SOF2 = 0xC2; // progressive DCT
    private const byte DHT = 0xC4;
    private const byte DQT = 0xDB;
    private const byte DRI = 0xDD;
    private const byte SOS = 0xDA;
    private const byte APP14 = 0xEE;

    // JPEG zig-zag scan order (ISO 10918-1 §A.3.6).
    private static readonly int[] ZigZag =
    [
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private sealed class Component
    {
        internal int Id;
        internal int H;
        internal int V;
        internal int QuantId;
        internal int DcTableId;
        internal int AcTableId;
        internal int Pred;
        internal int BlocksPerLine;
        internal int BlocksPerColumn;
        internal int BlocksPerLineForMcu;
        internal int BlocksPerColumnForMcu;
        internal int[] BlockData = [];
        internal byte[] Samples = [];
        internal int SampleStride;
    }

    private sealed class JpegContext
    {
        private readonly byte[] _data;
        private int _pos;

        private readonly int[]?[] _quant = new int[4][];
        private readonly HuffmanTable?[][] _huff = [new HuffmanTable?[4], new HuffmanTable?[4]];

        private int _width;
        private int _height;
        private bool _progressive;
        private int _restartInterval;
        private int _adobeTransform = -1;
        private bool _adobe;
        private Component[] _components = [];
        private int _maxH = 1;
        private int _maxV = 1;
        private int _mcusPerLine;
        private int _mcusPerColumn;

        // Scan-level entropy state.
        private int _bitBuffer;
        private int _bitsLeft;
        private bool _markerHit;
        private int _eobrun;
        private int _spectralStart;
        private int _spectralEnd;
        private int _successive;          // Al
        private int _successiveACState;
        private int _successiveACNextValue;

        internal JpegContext(byte[] data)
        {
            _data = data;
        }

        internal void Parse()
        {
            if (_data.Length < 2 || _data[0] != MarkerPrefix || _data[1] != SOI)
            {
                throw new ImageException("Not a valid JPEG: missing SOI marker.");
            }
            _pos = 2;

            while (_pos < _data.Length)
            {
                if (_data[_pos] != MarkerPrefix)
                {
                    _pos++;
                    continue;
                }

                // Skip fill bytes.
                while (_pos < _data.Length && _data[_pos] == MarkerPrefix)
                {
                    _pos++;
                }
                if (_pos >= _data.Length)
                {
                    break;
                }

                int marker = _data[_pos++];

                if (marker == EOI)
                {
                    break;
                }

                switch (marker)
                {
                    case SOF0:
                    case SOF1:
                        ParseSOF(progressive: false);
                        break;
                    case SOF2:
                        ParseSOF(progressive: true);
                        break;
                    case DHT:
                        ParseDHT();
                        break;
                    case DQT:
                        ParseDQT();
                        break;
                    case DRI:
                        ReadU16();
                        _restartInterval = ReadU16();
                        break;
                    case APP14:
                        ParseAdobe();
                        break;
                    case SOS:
                        ParseSOS();
                        break;
                    default:
                        SkipSegment();
                        break;
                }
            }
        }

        private void SkipSegment()
        {
            int len = ReadU16();
            _pos += len - 2;
        }

        private void ParseSOF(bool progressive)
        {
            _progressive = progressive;
            ReadU16(); // length
            int precision = _data[_pos++];
            if (precision != 8)
            {
                throw new ImageException($"JPEG precision {precision} not supported (only 8-bit).");
            }
            _height = ReadU16();
            _width = ReadU16();
            int n = _data[_pos++];
            _components = new Component[n];

            for (int i = 0; i < n; i++)
            {
                int id = _data[_pos++];
                int s = _data[_pos++];
                int q = _data[_pos++];
                Component c = new Component { Id = id, H = (s >> 4) & 0xF, V = s & 0xF, QuantId = q };
                _maxH = Math.Max(_maxH, c.H);
                _maxV = Math.Max(_maxV, c.V);
                _components[i] = c;
            }

            _mcusPerLine = (_width + 8 * _maxH - 1) / (8 * _maxH);
            _mcusPerColumn = (_height + 8 * _maxV - 1) / (8 * _maxV);

            foreach (Component c in _components)
            {
                c.BlocksPerLine = (int)Math.Ceiling(Math.Ceiling(_width / 8.0) * c.H / _maxH);
                c.BlocksPerColumn = (int)Math.Ceiling(Math.Ceiling(_height / 8.0) * c.V / _maxV);
                c.BlocksPerLineForMcu = _mcusPerLine * c.H;
                c.BlocksPerColumnForMcu = _mcusPerColumn * c.V;
                c.BlockData = new int[c.BlocksPerColumnForMcu * c.BlocksPerLineForMcu * 64];
            }
        }

        private void ParseDHT()
        {
            int len = ReadU16() - 2;
            int read = 0;
            while (read < len)
            {
                int tc = _data[_pos++];
                read++;
                int cls = (tc >> 4) & 0xF;
                int id = tc & 0xF;
                if (cls > 1 || id > 3)
                {
                    throw new ImageException($"Invalid Huffman table specifier: class={cls} id={id}.");
                }
                byte[] counts = new byte[16];
                int total = 0;
                for (int i = 0; i < 16; i++)
                {
                    counts[i] = _data[_pos++];
                    total += counts[i];
                }
                read += 16;
                byte[] symbols = new byte[total];
                Array.Copy(_data, _pos, symbols, 0, total);
                _pos += total;
                read += total;
                _huff[cls][id] = new HuffmanTable(counts, symbols);
            }
        }

        private void ParseDQT()
        {
            int len = ReadU16() - 2;
            int read = 0;
            while (read < len)
            {
                int pq = _data[_pos++];
                read++;
                int precision = (pq >> 4) & 0xF;
                int id = pq & 0xF;
                if (id > 3)
                {
                    throw new ImageException($"Invalid quantisation table ID: {id}.");
                }
                int[] table = new int[64];
                for (int i = 0; i < 64; i++)
                {
                    if (precision == 0)
                    {
                        table[i] = _data[_pos++];
                        read++;
                    }
                    else
                    {
                        table[i] = ReadU16();
                        read += 2;
                    }
                }
                _quant[id] = table;
            }
        }

        private void ParseAdobe()
        {
            int len = ReadU16() - 2;
            int start = _pos;
            if (len >= 12 && _data[start] == (byte)'A' && _data[start + 1] == (byte)'d'
                && _data[start + 2] == (byte)'o' && _data[start + 3] == (byte)'b' && _data[start + 4] == (byte)'e')
            {
                _adobe = true;
                _adobeTransform = _data[start + 11];
            }
            _pos = start + len;
        }

        // ── Scan decoding ─────────────────────────────────────────────────

        private void ParseSOS()
        {
            ReadU16(); // length
            int ns = _data[_pos++];
            Component[] scanComps = new Component[ns];
            for (int i = 0; i < ns; i++)
            {
                int cs = _data[_pos++];
                int tables = _data[_pos++];
                Component comp = FindComponent(cs);
                comp.DcTableId = (tables >> 4) & 0xF;
                comp.AcTableId = tables & 0xF;
                scanComps[i] = comp;
            }
            _spectralStart = _data[_pos++];
            _spectralEnd = _data[_pos++];
            int approx = _data[_pos++];
            int ah = (approx >> 4) & 0xF;
            _successive = approx & 0xF;

            DecodeScan(scanComps, ah);
        }

        private Component FindComponent(int id)
        {
            foreach (Component c in _components)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }
            throw new ImageException($"JPEG scan references unknown component {id}.");
        }

        private void DecodeScan(Component[] scanComps, int ah)
        {
            // Reset entropy state for this scan.
            _bitsLeft = 0;
            _markerHit = false;
            _eobrun = 0;
            _successiveACState = 0;
            foreach (Component c in _components)
            {
                c.Pred = 0;
            }

            bool interleaved = scanComps.Length > 1;
            int restarts = 0;

            if (!interleaved)
            {
                Component c = scanComps[0];
                int total = c.BlocksPerLine * c.BlocksPerColumn;
                for (int n = 0; n < total; n++)
                {
                    HandleRestart(ref restarts, scanComps);
                    int row = n / c.BlocksPerLine;
                    int col = n % c.BlocksPerLine;
                    int offset = (row * c.BlocksPerLineForMcu + col) * 64;
                    DecodeBlock(c, offset, ah);
                    restarts++;
                }
            }
            else
            {
                int totalMcus = _mcusPerLine * _mcusPerColumn;
                for (int mcu = 0; mcu < totalMcus; mcu++)
                {
                    HandleRestart(ref restarts, scanComps);
                    int mcuRow = mcu / _mcusPerLine;
                    int mcuCol = mcu % _mcusPerLine;
                    foreach (Component c in scanComps)
                    {
                        for (int v = 0; v < c.V; v++)
                        {
                            for (int h = 0; h < c.H; h++)
                            {
                                int blockRow = mcuRow * c.V + v;
                                int blockCol = mcuCol * c.H + h;
                                int offset = (blockRow * c.BlocksPerLineForMcu + blockCol) * 64;
                                DecodeBlock(c, offset, ah);
                            }
                        }
                    }
                    restarts++;
                }
            }

            // Advance past the entropy-coded data to the next marker.
            AlignToNextMarker();
        }

        private void HandleRestart(ref int restarts, Component[] scanComps)
        {
            if (_restartInterval <= 0 || restarts == 0 || restarts % _restartInterval != 0)
            {
                return;
            }

            // Align and consume the RST marker.
            _bitsLeft = 0;
            _markerHit = false;
            while (_pos + 1 < _data.Length)
            {
                if (_data[_pos] == MarkerPrefix && _data[_pos + 1] >= 0xD0 && _data[_pos + 1] <= 0xD7)
                {
                    _pos += 2;
                    break;
                }
                _pos++;
            }
            _eobrun = 0;
            _successiveACState = 0;
            foreach (Component c in scanComps)
            {
                c.Pred = 0;
            }
        }

        private void DecodeBlock(Component c, int offset, int ah)
        {
            if (!_progressive)
            {
                DecodeBaseline(c, offset);
            }
            else if (_spectralStart == 0)
            {
                if (ah == 0)
                {
                    DecodeDcFirst(c, offset);
                }
                else
                {
                    DecodeDcSuccessive(c, offset);
                }
            }
            else if (ah == 0)
            {
                DecodeAcFirst(c, offset);
            }
            else
            {
                DecodeAcSuccessive(c, offset);
            }
        }

        private void DecodeBaseline(Component c, int offset)
        {
            HuffmanTable dc = _huff[0][c.DcTableId] ?? throw new ImageException("Missing DC Huffman table.");
            HuffmanTable ac = _huff[1][c.AcTableId] ?? throw new ImageException("Missing AC Huffman table.");

            int t = dc.Decode(this);
            int diff = t == 0 ? 0 : ReceiveExtend(t);
            c.Pred += diff;
            c.BlockData[offset] = c.Pred;

            int k = 1;
            while (k < 64)
            {
                int rs = ac.Decode(this);
                int s = rs & 0xF;
                int r = rs >> 4;
                if (s == 0)
                {
                    if (r != 15)
                    {
                        break;
                    }
                    k += 16;
                    continue;
                }
                k += r;
                if (k >= 64)
                {
                    break;
                }
                c.BlockData[offset + ZigZag[k]] = ReceiveExtend(s);
                k++;
            }
        }

        private void DecodeDcFirst(Component c, int offset)
        {
            HuffmanTable dc = _huff[0][c.DcTableId] ?? throw new ImageException("Missing DC Huffman table.");
            int t = dc.Decode(this);
            int diff = t == 0 ? 0 : ReceiveExtend(t);
            c.Pred += diff;
            c.BlockData[offset] = c.Pred << _successive;
        }

        private void DecodeDcSuccessive(Component c, int offset)
        {
            if (ReadBit() == 1)
            {
                c.BlockData[offset] |= 1 << _successive;
            }
        }

        private void DecodeAcFirst(Component c, int offset)
        {
            if (_eobrun > 0)
            {
                _eobrun--;
                return;
            }

            HuffmanTable ac = _huff[1][c.AcTableId] ?? throw new ImageException("Missing AC Huffman table.");
            int k = _spectralStart;
            int e = _spectralEnd;
            while (k <= e)
            {
                int rs = ac.Decode(this);
                int s = rs & 0xF;
                int r = rs >> 4;
                if (s == 0)
                {
                    if (r < 15)
                    {
                        _eobrun = ReadBits(r) + (1 << r) - 1;
                        break;
                    }
                    k += 16;
                    continue;
                }
                k += r;
                if (k > e)
                {
                    break;
                }
                c.BlockData[offset + ZigZag[k]] = ReceiveExtend(s) * (1 << _successive);
                k++;
            }
        }

        private void DecodeAcSuccessive(Component c, int offset)
        {
            HuffmanTable ac = _huff[1][c.AcTableId] ?? throw new ImageException("Missing AC Huffman table.");
            int k = _spectralStart;
            int e = _spectralEnd;
            int r = 0;
            while (k <= e)
            {
                int z = offset + ZigZag[k];
                int sign = c.BlockData[z] < 0 ? -1 : 1;
                switch (_successiveACState)
                {
                    case 0:
                        int rs = ac.Decode(this);
                        int s = rs & 0xF;
                        r = rs >> 4;
                        if (s == 0)
                        {
                            if (r < 15)
                            {
                                _eobrun = ReadBits(r) + (1 << r);
                                _successiveACState = 4;
                            }
                            else
                            {
                                r = 16;
                                _successiveACState = 1;
                            }
                        }
                        else
                        {
                            _successiveACNextValue = ReceiveExtend(s);
                            _successiveACState = r != 0 ? 2 : 3;
                        }
                        continue;
                    case 1:
                    case 2:
                        if (c.BlockData[z] != 0)
                        {
                            c.BlockData[z] += sign * (ReadBit() << _successive);
                        }
                        else
                        {
                            r--;
                            if (r == 0)
                            {
                                _successiveACState = _successiveACState == 2 ? 3 : 0;
                            }
                        }
                        break;
                    case 3:
                        if (c.BlockData[z] != 0)
                        {
                            c.BlockData[z] += sign * (ReadBit() << _successive);
                        }
                        else
                        {
                            c.BlockData[z] = _successiveACNextValue << _successive;
                            _successiveACState = 0;
                        }
                        break;
                    case 4:
                        if (c.BlockData[z] != 0)
                        {
                            c.BlockData[z] += sign * (ReadBit() << _successive);
                        }
                        break;
                    default:
                        break;
                }
                k++;
            }

            if (_successiveACState == 4)
            {
                _eobrun--;
                if (_eobrun == 0)
                {
                    _successiveACState = 0;
                }
            }
        }

        // ── Entropy bit reader (called by HuffmanTable.Decode) ────────────

        internal int ReadBit()
        {
            if (_bitsLeft > 0)
            {
                _bitsLeft--;
                return (_bitBuffer >> _bitsLeft) & 1;
            }
            if (_markerHit || _pos >= _data.Length)
            {
                return 0;
            }
            _bitBuffer = _data[_pos++];
            if (_bitBuffer == 0xFF)
            {
                int next = _pos < _data.Length ? _data[_pos] : 0;
                if (next == 0x00)
                {
                    _pos++; // byte stuffing
                }
                else
                {
                    // A real marker — stop feeding bits for this scan.
                    _markerHit = true;
                    _pos--; // leave _pos on the 0xFF for the marker scan
                    _bitBuffer = 0;
                    return 0;
                }
            }
            _bitsLeft = 7;
            return (_bitBuffer >> 7) & 1;
        }

        private int ReadBits(int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++)
            {
                v = (v << 1) | ReadBit();
            }
            return v;
        }

        private int ReceiveExtend(int count)
        {
            int v = ReadBits(count);
            if (v < (1 << (count - 1)))
            {
                v -= (1 << count) - 1;
            }
            return v;
        }

        private void AlignToNextMarker()
        {
            _bitsLeft = 0;
            // If a marker was already detected during bit reading, _pos sits on
            // the 0xFF. Otherwise scan forward to the next non-stuffed marker.
            while (_pos + 1 < _data.Length)
            {
                if (_data[_pos] == MarkerPrefix)
                {
                    int next = _data[_pos + 1];
                    if (next != 0x00 && !(next >= 0xD0 && next <= 0xD7))
                    {
                        return; // _pos on 0xFF of the next real marker
                    }
                }
                _pos++;
            }
        }

        // ── Frame assembly ────────────────────────────────────────────────

        internal ImageFrame BuildFrame()
        {
            if (_width <= 0 || _height <= 0 || _components.Length == 0)
            {
                throw new ImageException("JPEG frame header was not decoded — invalid or unsupported JPEG.");
            }

            // Dequantise + IDCT each component's blocks into a sample plane.
            foreach (Component c in _components)
            {
                int[]? qt = _quant[c.QuantId < 4 ? c.QuantId : 0];
                int strideBlocks = c.BlocksPerLineForMcu;
                c.SampleStride = strideBlocks * 8;
                c.Samples = new byte[c.SampleStride * c.BlocksPerColumnForMcu * 8];

                int[] block = new int[64];
                for (int by = 0; by < c.BlocksPerColumn; by++)
                {
                    for (int bx = 0; bx < c.BlocksPerLine; bx++)
                    {
                        int off = (by * strideBlocks + bx) * 64;
                        if (qt is not null)
                        {
                            for (int k = 0; k < 64; k++)
                            {
                                block[ZigZag[k]] = c.BlockData[off + ZigZag[k]] * qt[k];
                            }
                        }
                        else
                        {
                            Array.Copy(c.BlockData, off, block, 0, 64);
                        }

                        byte[] px = IDCT(block);
                        int px0 = by * 8 * c.SampleStride + bx * 8;
                        for (int y = 0; y < 8; y++)
                        {
                            Array.Copy(px, y * 8, c.Samples, px0 + y * c.SampleStride, 8);
                        }
                    }
                }
            }

            PixelBuffer buffer = new PixelBuffer(_width, _height);
            int nc = _components.Length;

            if (nc == 1)
            {
                Component y = _components[0];
                for (int j = 0; j < _height; j++)
                {
                    for (int i = 0; i < _width; i++)
                    {
                        byte g = Sample(y, i, j);
                        buffer.SetPixelBgra(i, j, g, g, g, 255);
                    }
                }
                return new ImageFrame(buffer, ImageColorFormat.Gray8);
            }

            if (nc == 3)
            {
                bool rgb = _adobeTransform == 0 || (_adobeTransform < 0 && IsRgbIds());
                Component c0 = _components[0], c1 = _components[1], c2 = _components[2];
                for (int j = 0; j < _height; j++)
                {
                    for (int i = 0; i < _width; i++)
                    {
                        int a = Sample(c0, i, j);
                        int b = Sample(c1, i, j);
                        int cc = Sample(c2, i, j);
                        (byte r, byte g, byte bl) = rgb
                            ? ((byte)a, (byte)b, (byte)cc)
                            : YccToRgb(a, b, cc);
                        buffer.SetPixelBgra(i, j, bl, g, r, 255);
                    }
                }
                return new ImageFrame(buffer, ImageColorFormat.Rgb24);
            }

            if (nc == 4)
            {
                bool ycck = _adobeTransform == 2;
                Component c0 = _components[0], c1 = _components[1], c2 = _components[2], c3 = _components[3];
                for (int j = 0; j < _height; j++)
                {
                    for (int i = 0; i < _width; i++)
                    {
                        int s0 = Sample(c0, i, j);
                        int s1 = Sample(c1, i, j);
                        int s2 = Sample(c2, i, j);
                        int kCh = Sample(c3, i, j);

                        int cyan, magenta, yellow;
                        if (ycck)
                        {
                            (byte r, byte g, byte b) = YccToRgb(s0, s1, s2);
                            cyan = 255 - r;
                            magenta = 255 - g;
                            yellow = 255 - b;
                        }
                        else
                        {
                            cyan = s0;
                            magenta = s1;
                            yellow = s2;
                        }

                        // Adobe stores CMYK channels inverted.
                        if (_adobe)
                        {
                            cyan = 255 - cyan;
                            magenta = 255 - magenta;
                            yellow = 255 - yellow;
                            kCh = 255 - kCh;
                        }

                        int rr = (255 - cyan) * (255 - kCh) / 255;
                        int gg = (255 - magenta) * (255 - kCh) / 255;
                        int bb = (255 - yellow) * (255 - kCh) / 255;
                        buffer.SetPixelBgra(i, j, (byte)bb, (byte)gg, (byte)rr, 255);
                    }
                }
                return new ImageFrame(buffer, ImageColorFormat.Rgb24);
            }

            throw new ImageException($"JPEG with {nc} components is not supported.");
        }

        private bool IsRgbIds()
        {
            return _components.Length == 3
                && _components[0].Id == 'R' && _components[1].Id == 'G' && _components[2].Id == 'B';
        }

        private byte Sample(Component c, int x, int y)
        {
            int cx = x * c.H / _maxH;
            int cy = y * c.V / _maxV;
            int idx = cy * c.SampleStride + cx;
            return idx >= 0 && idx < c.Samples.Length ? c.Samples[idx] : (byte)0;
        }

        private static (byte R, byte G, byte B) YccToRgb(int y, int cb, int cr)
        {
            int r = (int)(y + 1.402 * (cr - 128));
            int g = (int)(y - 0.344136 * (cb - 128) - 0.714136 * (cr - 128));
            int b = (int)(y + 1.772 * (cb - 128));
            return ((byte)Clip(r), (byte)Clip(g), (byte)Clip(b));
        }

        private static int Clip(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        private int ReadU16()
        {
            int hi = _data[_pos++];
            int lo = _data[_pos++];
            return (hi << 8) | lo;
        }

        // ── IDCT (direct separable; level-shifted, clamped 8-bit output) ──

        private static readonly double[,] IdctCos = BuildIdctCos();

        private static double[,] BuildIdctCos()
        {
            double[,] c = new double[8, 8];
            for (int u = 0; u < 8; u++)
            {
                double cu = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
                for (int x = 0; x < 8; x++)
                {
                    c[u, x] = cu * Math.Cos((2 * x + 1) * u * Math.PI / 16.0);
                }
            }
            return c;
        }

        private static byte[] IDCT(int[] input)
        {
            double[] tmp = new double[64];
            byte[] output = new byte[64];

            for (int row = 0; row < 8; row++)
            {
                int off = row * 8;
                for (int x = 0; x < 8; x++)
                {
                    double sum = 0.0;
                    for (int u = 0; u < 8; u++)
                    {
                        sum += IdctCos[u, x] * input[off + u];
                    }
                    tmp[off + x] = sum;
                }
            }

            for (int col = 0; col < 8; col++)
            {
                for (int y = 0; y < 8; y++)
                {
                    double sum = 0.0;
                    for (int v = 0; v < 8; v++)
                    {
                        sum += IdctCos[v, y] * tmp[col + v * 8];
                    }
                    int val = (int)(sum * 0.25 + 128.5);
                    output[y * 8 + col] = (byte)(val < 0 ? 0 : val > 255 ? 255 : val);
                }
            }

            return output;
        }
    }

    // ── Huffman table (ISO 10918-1 §F.2) ─────────────────────────────────

    private sealed class HuffmanTable
    {
        private readonly int[] _counts;
        private readonly int[] _maxCodes;
        private readonly int[] _minCodes;
        private readonly int[] _valPtrs;
        private readonly byte[] _symbols;

        internal HuffmanTable(byte[] counts, byte[] symbols)
        {
            _symbols = symbols;
            _counts = new int[16];
            _maxCodes = new int[17];
            _minCodes = new int[16];
            _valPtrs = new int[16];

            int code = 0;
            int si = 0;
            for (int len = 0; len < 16; len++)
            {
                _minCodes[len] = code;
                _counts[len] = counts[len];
                for (int k = 0; k < counts[len]; k++, si++)
                {
                    code++;
                }
                _maxCodes[len] = code;
                _valPtrs[len] = si - counts[len];
                code <<= 1;
            }
            _maxCodes[16] = int.MaxValue;
        }

        internal int Decode(JpegContext bits)
        {
            int code = 0;
            for (int len = 0; len < 16; len++)
            {
                code = (code << 1) | bits.ReadBit();
                if (_counts[len] > 0 && code >= _minCodes[len] && code < _maxCodes[len])
                {
                    int idx = _valPtrs[len] + code - _minCodes[len];
                    if (idx >= 0 && idx < _symbols.Length)
                    {
                        return _symbols[idx];
                    }
                }
            }
            return 0;
        }
    }
}
