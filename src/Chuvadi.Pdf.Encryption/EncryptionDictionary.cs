// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.1 — Encryption dictionary
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption
//
// Parses the /Encrypt dictionary in a PDF's trailer into a strongly-typed
// view used by the rest of the encryption subsystem.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Parsed view of a PDF's /Encrypt trailer entry. Identifies the algorithm,
/// key length, password verification values, and permission flags.
/// </summary>
public sealed class EncryptionDictionary
{
    // A /CFM that is missing or not known.
    private const EncryptionAlgorithm UnknownCfm = (EncryptionAlgorithm)(-1);

    private EncryptionDictionary()
    {
    }

    /// <summary>Encryption algorithm in use.</summary>
    public EncryptionAlgorithm Algorithm { get; private set; }

    /// <summary>/V entry (algorithm version 1..5).</summary>
    public int V { get; private set; }

    /// <summary>/R entry (revision 2..6).</summary>
    public int R { get; private set; }

    /// <summary>/Length entry: key length in bits.</summary>
    public int KeyBits { get; private set; }

    /// <summary>Key length in bytes (KeyBits / 8).</summary>
    public int KeyBytes => KeyBits / 8;

    /// <summary>/P entry: permission flags.</summary>
    public int Permissions { get; private set; }

    /// <summary>/O entry: owner-password verification bytes.</summary>
    public byte[] O { get; private set; } = Array.Empty<byte>();

    /// <summary>/U entry: user-password verification bytes.</summary>
    public byte[] U { get; private set; } = Array.Empty<byte>();

    /// <summary>/OE entry (R=6 only): encrypted file key from owner password.</summary>
    public byte[] OE { get; private set; } = Array.Empty<byte>();

    /// <summary>/UE entry (R=6 only): encrypted file key from user password.</summary>
    public byte[] UE { get; private set; } = Array.Empty<byte>();

    /// <summary>/Perms entry (R=6 only): encrypted permissions check.</summary>
    public byte[] Perms { get; private set; } = Array.Empty<byte>();

    /// <summary>/EncryptMetadata entry (default true).</summary>
    public bool EncryptMetadata { get; private set; } = true;

    // 3.34.0 (#52d): the algorithms the crypt filters named by /StmF, /StrF
    // and /EFF select (ISO 32000-1 §7.6.5). /Identity selects None. Before
    // V4 there are no crypt filters and all three are Algorithm. A missing
    // /StmF or /StrF, or a name that /CF does not define with a known /CFM,
    // keeps Algorithm (the behaviour before 3.34.0); a missing /EFF follows
    // /StmF, as the standard says.
    internal EncryptionAlgorithm StreamAlgorithm { get; private set; }

    internal EncryptionAlgorithm StringAlgorithm { get; private set; }

    internal EncryptionAlgorithm EmbeddedFileAlgorithm { get; private set; }

    // The crypt filters /CF defines, by name, with a known /CFM, plus
    // /Identity; a stream's own /Crypt filter names one of them (§7.4.10).
    internal Dictionary<string, EncryptionAlgorithm> CryptFilters { get; } =
        new Dictionary<string, EncryptionAlgorithm>(StringComparer.Ordinal)
        {
            ["Identity"] = EncryptionAlgorithm.None,
        };

    // True when strings and streams are not encrypted (both /Identity), so
    // only embedded files are: the document itself can be read without the
    // password (ISO 32000-1 §7.6.5, the unencrypted wrapper).
    internal bool OnlyEmbeddedFilesEncrypted =>
        StreamAlgorithm == EncryptionAlgorithm.None && StringAlgorithm == EncryptionAlgorithm.None;

    /// <summary>
    /// Parses an /Encrypt dictionary. Returns null when the dictionary is missing
    /// or uses an unsupported security handler.
    /// </summary>
    public static EncryptionDictionary? Parse(PdfDictionary? dict)
    {
        if (dict is null)
        {
            return null;
        }

        // Filter must be /Standard
        PdfName? filter = dict.GetName(PdfName.Intern("Filter"));
        if (filter is null || filter.Value != "Standard")
        {
            return null;
        }

        EncryptionDictionary e = new EncryptionDictionary();

        e.V = GetInt(dict, "V", 0);
        e.R = GetInt(dict, "R", 0);
        e.KeyBits = GetInt(dict, "Length", 40);
        e.Permissions = GetPermissions(dict);
        e.O = GetBytes(dict, "O");
        e.U = GetBytes(dict, "U");
        e.OE = GetBytes(dict, "OE");
        e.UE = GetBytes(dict, "UE");
        e.Perms = GetBytes(dict, "Perms");

        if (dict.TryGetValue(PdfName.Intern("EncryptMetadata"), out PdfPrimitive? emp) &&
            emp is PdfBoolean emb)
        {
            e.EncryptMetadata = emb.Value;
        }

        // Determine algorithm
        e.Algorithm = (e.V, e.R) switch
        {
            (1, 2) => EncryptionAlgorithm.Rc4_40,
            (2, _) => EncryptionAlgorithm.Rc4_128,
            (4, _) => DetectV4Cfm(dict),
            (5, _) => EncryptionAlgorithm.Aes_256,
            _ => EncryptionAlgorithm.None,
        };

        if (e.Algorithm == EncryptionAlgorithm.None)
        {
            return null;
        }

        e.StreamAlgorithm = e.Algorithm;
        e.StringAlgorithm = e.Algorithm;
        e.EmbeddedFileAlgorithm = e.Algorithm;
        if (e.V >= 4)
        {
            e.StreamAlgorithm = CryptFilter(dict, "StmF", e.Algorithm);
            e.StringAlgorithm = CryptFilter(dict, "StrF", e.Algorithm);
            e.EmbeddedFileAlgorithm = CryptFilter(dict, "EFF", e.StreamAlgorithm);
            if (dict.TryGetValue(PdfName.Intern("CF"), out PdfPrimitive? cfPrim) && cfPrim is PdfDictionary cf)
            {
                foreach (KeyValuePair<PdfName, PdfPrimitive> entry in cf)
                {
                    EncryptionAlgorithm algorithm = Cfm(entry.Value as PdfDictionary);
                    if (algorithm != UnknownCfm)
                    {
                        e.CryptFilters[entry.Key.Value] = algorithm;
                    }
                }
            }
        }

        return e;
    }

    // The algorithm of the crypt filter that the name in /key selects.
    private static EncryptionAlgorithm CryptFilter(PdfDictionary dict, string key, EncryptionAlgorithm fallback)
    {
        PdfName? name = dict.GetName(PdfName.Intern(key));
        if (name is null)
        {
            return fallback;
        }

        if (name.Value == "Identity")
        {
            return EncryptionAlgorithm.None;
        }

        if (!dict.TryGetValue(PdfName.Intern("CF"), out PdfPrimitive? cfPrim)
            || cfPrim is not PdfDictionary cf
            || !cf.TryGetValue(name, out PdfPrimitive? filterPrim)
            || filterPrim is not PdfDictionary filter)
        {
            return fallback;
        }

        EncryptionAlgorithm algorithm = Cfm(filter);
        return algorithm == UnknownCfm ? fallback : algorithm;
    }

    // The algorithm a crypt filter dictionary's /CFM selects, or -1 when it
    // is missing or not known.
    private static EncryptionAlgorithm Cfm(PdfDictionary? filter)
    {
        PdfName? cfm = filter?.GetName(PdfName.Intern("CFM"));
        return cfm?.Value switch
        {
            "None" => EncryptionAlgorithm.None,
            "V2" => EncryptionAlgorithm.Rc4_128,
            "AESV2" => EncryptionAlgorithm.Aes_128,
            "AESV3" => EncryptionAlgorithm.Aes_256,
            _ => UnknownCfm,
        };
    }

    private static EncryptionAlgorithm DetectV4Cfm(PdfDictionary dict)
    {
        // V=4 uses /CF (crypt filters). Look for the StdCF entry and its CFM value.
        if (!dict.TryGetValue(PdfName.Intern("CF"), out PdfPrimitive? cfPrim) ||
            cfPrim is not PdfDictionary cf)
        {
            return EncryptionAlgorithm.None;
        }

        if (!cf.TryGetValue(PdfName.Intern("StdCF"), out PdfPrimitive? stdPrim) ||
            stdPrim is not PdfDictionary std)
        {
            return EncryptionAlgorithm.None;
        }

        PdfName? cfm = std.GetName(PdfName.Intern("CFM"));
        if (cfm is null)
        {
            return EncryptionAlgorithm.None;
        }

        return cfm.Value switch
        {
            "V2" => EncryptionAlgorithm.Rc4_128,
            "AESV2" => EncryptionAlgorithm.Aes_128,
            _ => EncryptionAlgorithm.None,
        };
    }

    // /P is a 32-bit field. A writer that stores it unsigned (pypdf writes
    // 4294963392 for -3904) gives a number above the 32-bit range, which the
    // parser keeps as a real (3.32.0, D-278); its low 32 bits are the field.
    private static int GetPermissions(PdfDictionary dict)
    {
        if (dict.TryGetValue(PdfName.Intern("P"), out PdfPrimitive? p))
        {
            if (p is PdfInteger i)
            {
                return i.Value;
            }

            if (p is PdfReal r && double.IsFinite(r.Value) && Math.Abs(r.Value) < 9.2e18 && r.Value == Math.Floor(r.Value))
            {
                return unchecked((int)(long)r.Value);
            }
        }

        return -3904;
    }

    private static int GetInt(PdfDictionary dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? p) && p is PdfInteger i)
        {
            return i.Value;
        }
        return defaultValue;
    }

    private static byte[] GetBytes(PdfDictionary dict, string key)
    {
        if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? p) && p is PdfString s)
        {
            return s.Bytes;
        }
        return Array.Empty<byte>();
    }
}
