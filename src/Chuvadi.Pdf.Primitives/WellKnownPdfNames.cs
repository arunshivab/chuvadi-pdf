// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// The names Chuvadi itself uses (3.31.2, D-219). Each one always resolves to
/// the same <see cref="PdfName"/> instance, so lookups with them never
/// allocate. Other names are cached up to a fixed limit; see
/// <see cref="PdfName.Intern(string)"/>.
/// </summary>
/// <remarks>
/// Generated from every <c>PdfName.Intern("...")</c> literal in <c>src</c> and
/// the static fields of <see cref="PdfName"/>. A name missing from this list
/// still works the same; it only goes through the bounded cache instead.
/// </remarks>
internal static class WellKnownPdfNames
{
    /// <summary>The well-known names, without the leading solidus.</summary>
    internal static readonly string[] Names =
    {
        "0.0", "A", "AA", "AcroForm", "Action", "ActualText", "adbe.pkcs7.detached",
        "Adobe.PPKLite", "AESV2", "AESV3", "AF", "Alt", "Alternate", "Annot", "Annots", "AP", "AS",
        "Ascent", "AuthEvent", "Author", "BaseEncoding", "BaseFont", "BaseState", "BBox", "BC",
        "BitsPerComponent", "BitsPerCoordinate", "BitsPerFlag", "BitsPerSample", "BM", "Border",
        "Bounds", "BS", "ByteRange", "C", "CA", "ca", "CapHeight", "Catalog", "Category", "Cert",
        "Certs", "CF", "CFM", "CIDFontType0C", "CIDFontType2", "CIDSystemInfo", "CIDToGIDMap",
        "Collection", "Colors", "ColorSpace", "Columns", "ContactInfo", "Contents", "Coords",
        "Count", "CreationDate", "Creator", "CRL", "CRLs", "CropBox", "CS", "CvZaDb", "D", "DA",
        "DCTDecode", "Decode", "DecodeParms", "DescendantFonts", "Descent", "Dest",
        "DestOutputProfile", "Dests", "DeviceCMYK", "DeviceGray", "DeviceRGB", "Differences",
        "DocOpen", "DocTimeStamp", "Domain", "DP", "DSS", "DW", "E", "EmbeddedFiles", "Encode",
        "Encoding", "Encrypt", "EncryptMetadata", "ETSI.RFC3161", "Event", "Extend", "ExtGState",
        "F", "Fields", "Filter", "First", "FirstChar", "Fit", "Flags", "FlateDecode", "Font",
        "FontBBox", "FontDescriptor", "FontFile", "FontFile2", "FontFile3", "FontName", "Form",
        "FormType", "FT", "Function", "Functions", "FunctionType", "G", "Gamma", "GoTo", "Group",
        "GTS_PDFA1", "H", "Height", "Helvetica", "IC", "ID", "Identity", "Identity-H", "Image",
        "ImageB", "ImageC", "ImageMask", "Index", "Indexed", "Info", "InkList", "Intent",
        "Interpolate", "ItalicAngle", "JavaScript", "JBIG2Globals", "K", "Keywords", "Kids", "L",
        "Last", "LastChar", "LE", "Length", "Length1", "Linearized", "Link", "Location", "LW", "M",
        "Marked", "MarkInfo", "Mask", "Matrix", "Matte", "MCID", "MediaBox", "Metadata",
        "MissingWidth", "ML", "ModDate", "N", "Name", "Names", "NeedAppearances", "Next", "O",
        "Obj", "ObjStm", "OC", "OCGs", "OCProperties", "OCSP", "OCSPs", "OE", "OFF", "Off", "ON",
        "Open", "OpenAction", "Ordering", "Outlines", "OutputConditionIdentifier", "OutputIntent",
        "OutputIntents", "P", "Page", "Pages", "Params", "Parent", "Pattern", "PatternType", "PDF",
        "Perms", "Pg", "PieceInfo", "Predictor", "Prev", "ProcSet", "Producer", "Properties",
        "QuadPoints", "R", "Range", "Reason", "Rect", "Registry", "RegistryName", "Resources",
        "Root", "Rotate", "S", "Shading", "ShadingType", "Sig", "SigFlags", "Size", "SMask",
        "SMaskInData", "Standard", "StdCF", "StemV", "Stm", "StmF", "StrF", "StructParent",
        "StructTreeRoot", "SubFilter", "Subject", "Subtype", "Supplement", "T", "Text", "Thumb",
        "Title", "ToUnicode", "Trapped", "TrueType", "Type", "Type0", "Type1", "U", "UE", "URI",
        "Usage", "UserUnit", "V", "VE", "Version", "Vertices", "VerticesPerRow", "View",
        "ViewState", "VP", "VRI", "W", "WhitePoint", "Widget", "Width", "Widths", "WinAnsiEncoding",
        "XFA", "XML", "XObject", "XRef", "XRefStm", "XStep", "XYZ", "YStep", "ZapfDingbats",
    };
}
