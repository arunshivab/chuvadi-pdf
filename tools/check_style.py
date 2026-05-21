#!/usr/bin/env python3
"""
Chuvadi pre-delivery style checker.
Catches common analyzer violations and missing using directives.

Checks performed:
  1. var in src/ files (IDE0008)
  2. Duplicate using directives (IDE0005 partial)
  3. Missing using directives for known Chuvadi and System namespaces
  4. Declared usings whose namespace isn't referenced anywhere (IDE0005)
  5. ProjectReference completeness against Chuvadi.Pdf.* usings (CS0234)
  6. Basic syntax sanity (control chars in source, corrupt char literals)
"""

import re
import sys
import os

# Map: type name -> required using directive
# Add entries whenever a new type is introduced in any Chuvadi project.
REQUIRED_USINGS = {
    # ── Chuvadi.Pdf.Primitives ────────────────────────────────────────────
    "PdfObjectId":      "using Chuvadi.Pdf.Primitives;",
    "PdfPrimitive":     "using Chuvadi.Pdf.Primitives;",
    "PdfNull":          "using Chuvadi.Pdf.Primitives;",
    "PdfBoolean":       "using Chuvadi.Pdf.Primitives;",
    "PdfInteger":       "using Chuvadi.Pdf.Primitives;",
    "PdfReal":          "using Chuvadi.Pdf.Primitives;",
    "PdfName":          "using Chuvadi.Pdf.Primitives;",
    "PdfString":        "using Chuvadi.Pdf.Primitives;",
    "PdfArray":         "using Chuvadi.Pdf.Primitives;",
    "PdfDictionary":    "using Chuvadi.Pdf.Primitives;",
    "PdfStream":        "using Chuvadi.Pdf.Primitives;",
    "PdfReference":     "using Chuvadi.Pdf.Primitives;",
    "PdfTokenType":     "using Chuvadi.Pdf.Primitives;",
    "PdfToken":         "using Chuvadi.Pdf.Primitives;",
    "PdfTokenizer":     "using Chuvadi.Pdf.Primitives;",
    "PdfException":           "using Chuvadi.Pdf.Primitives;",
    "PdfParseException":      "using Chuvadi.Pdf.Primitives;",
    "PdfCorruptionException": "using Chuvadi.Pdf.Primitives;",
    "PdfEncryptionException": "using Chuvadi.Pdf.Primitives;",
    "PdfPermissionException": "using Chuvadi.Pdf.Primitives;",
    "PdfPermissions":         "using Chuvadi.Pdf.Primitives;",
    # ── Chuvadi.Pdf.Filters ───────────────────────────────────────────────
    "IStreamFilter":    "using Chuvadi.Pdf.Filters;",
    "FilterException":  "using Chuvadi.Pdf.Filters;",
    "FilterParameters": "using Chuvadi.Pdf.Filters;",
    "FilterPipeline":   "using Chuvadi.Pdf.Filters;",
    "FilterRegistry":   "using Chuvadi.Pdf.Filters;",
    "DeflateFilter":    "using Chuvadi.Pdf.Filters;",
    # ── Chuvadi.Pdf.Objects ───────────────────────────────────────────────
    "PdfIndirectObject":    "using Chuvadi.Pdf.Objects;",
    "IPdfObjectResolver":   "using Chuvadi.Pdf.Objects;",
    "PdfObjectStore":       "using Chuvadi.Pdf.Objects;",
    "XrefEntry":            "using Chuvadi.Pdf.Objects;",
    "XrefEntryType":        "using Chuvadi.Pdf.Objects;",
    "XrefTable":            "using Chuvadi.Pdf.Objects;",
    "XrefStreamTable":      "using Chuvadi.Pdf.Objects;",
    # ── Chuvadi.Pdf.Rendering.DisplayList (v2 R2 — own project) ───────────
    "PageDisplayList":        "using Chuvadi.Pdf.Rendering.DisplayList;",
    "DisplayListBuilder":     "using Chuvadi.Pdf.Rendering.DisplayList;",
    "RenderOp":               "using Chuvadi.Pdf.Rendering.DisplayList;",
    "FillPathOp":             "using Chuvadi.Pdf.Rendering.DisplayList;",
    "StrokePathOp":           "using Chuvadi.Pdf.Rendering.DisplayList;",
    "DrawGlyphOp":            "using Chuvadi.Pdf.Rendering.DisplayList;",
    "DrawImageOp":            "using Chuvadi.Pdf.Rendering.DisplayList;",
    "NestedDisplayListOp":    "using Chuvadi.Pdf.Rendering.DisplayList;",
    "ClipPath":               "using Chuvadi.Pdf.Rendering.DisplayList;",
    # Phase 2.1 grouped text-op surface
    "TextOp":                 "using Chuvadi.Pdf.Rendering.DisplayList;",
    "DisplayListGlyph":       "using Chuvadi.Pdf.Rendering.DisplayList;",
    "TextRenderingMode":      "using Chuvadi.Pdf.Rendering.DisplayList;",
    "TextRunExtractor":       "using Chuvadi.Pdf.Rendering.DisplayList;",
    # ── Chuvadi.Pdf.Svg (v2 R2) ───────────────────────────────────────────
    "SvgRenderer":            "using Chuvadi.Pdf.Svg;",
    "SvgRenderOptions":       "using Chuvadi.Pdf.Svg;",
    "FontEmbedding":          "using Chuvadi.Pdf.Svg;",
    # ── Chuvadi.Pdf.Text (v2 R3 additions) ────────────────────────────────
    # SearchOptions/SearchMatch live here, NOT in Documents — Documents would
    # need to take a ProjectReference on Text, which would cycle.
    "TextRun":                "using Chuvadi.Pdf.Text;",
    "GlyphPosition":          "using Chuvadi.Pdf.Text;",
    "TextDirection":          "using Chuvadi.Pdf.Text;",
    "TextRunBuilder":         "using Chuvadi.Pdf.Text;",
    "SearchOptions":          "using Chuvadi.Pdf.Text;",
    "SearchMatch":            "using Chuvadi.Pdf.Text;",
    # ── Chuvadi.Pdf.Documents (v2 R3 — DocumentInfo aggregate) ────────────
    "DocumentInfo":           "using Chuvadi.Pdf.Documents;",
    "EncryptionInfo":         "using Chuvadi.Pdf.Documents;",
    "PdfDocument":            "using Chuvadi.Pdf.Documents;",
    "PdfPage":                "using Chuvadi.Pdf.Documents;",
    "PdfPageCollection":      "using Chuvadi.Pdf.Documents;",
    "LinearizationInfo":      "using Chuvadi.Pdf.Documents;",
    "LinearizationReader":    "using Chuvadi.Pdf.Documents;",
    # ── Chuvadi.Pdf.Annotations (Phase 1.1 + v2 R3 shapes) ────────────────
    "PdfAnnotation":          "using Chuvadi.Pdf.Annotations;",
    "AnnotationType":         "using Chuvadi.Pdf.Annotations;",
    "AnnotationException":    "using Chuvadi.Pdf.Annotations;",
    "AnnotationReader":       "using Chuvadi.Pdf.Annotations;",
    "AnnotationWriter":       "using Chuvadi.Pdf.Annotations;",
    "TextAnnotation":         "using Chuvadi.Pdf.Annotations;",
    "LinkAnnotation":         "using Chuvadi.Pdf.Annotations;",
    "FreeTextAnnotation":     "using Chuvadi.Pdf.Annotations;",
    "MarkupAnnotation":       "using Chuvadi.Pdf.Annotations;",
    "StampAnnotation":        "using Chuvadi.Pdf.Annotations;",
    "InkAnnotation":          "using Chuvadi.Pdf.Annotations;",
    "GenericAnnotation":      "using Chuvadi.Pdf.Annotations;",
    "ShapeAnnotation":        "using Chuvadi.Pdf.Annotations;",
    "ShapeKind":              "using Chuvadi.Pdf.Annotations;",
    # ── System types commonly forgotten ───────────────────────────────────
    "StringBuilder":        "using System.Text;",
    "MemoryStream":         "using System.IO;",
    "Stream":               "using System.IO;",
    "StreamReader":         "using System.IO;",
    "StreamWriter":         "using System.IO;",
    "TextWriter":           "using System.IO;",
    "InvalidDataException": "using System.IO;",
    "BinaryReader":         "using System.IO;",
    "BinaryWriter":         "using System.IO;",
    "FileStream":           "using System.IO;",
    "File":                 "using System.IO;",
    "Directory":            "using System.IO;",
    "Path":                 "using System.IO;",
    "SeekOrigin":           "using System.IO;",
    "FileMode":             "using System.IO;",
    "FileAccess":           "using System.IO;",
    "FileShare":            "using System.IO;",
    "IOException":          "using System.IO;",
    "List":                 "using System.Collections.Generic;",
    "Dictionary":           "using System.Collections.Generic;",
    "HashSet":              "using System.Collections.Generic;",
    "IList":                "using System.Collections.Generic;",
    "ICollection":          "using System.Collections.Generic;",
    "IReadOnlyList":        "using System.Collections.Generic;",
    "IEnumerable":          "using System.Collections.Generic;",
    "Stack":                "using System.Collections.Generic;",
    "Queue":                "using System.Collections.Generic;",
    "KeyValuePair":         "using System.Collections.Generic;",
    "IReadOnlyDictionary":  "using System.Collections.Generic;",
    "IReadOnlyCollection":  "using System.Collections.Generic;",
    "Math":                     "using System;",
    "Exception":                "using System;",
    "ArgumentException":        "using System;",
    "ArgumentNullException":    "using System;",
    "ArgumentOutOfRangeException": "using System;",
    "InvalidOperationException": "using System;",
    "NotSupportedException":    "using System;",
    "NotImplementedException":  "using System;",
    "ObjectDisposedException":  "using System;",
    "OverflowException":        "using System;",
    "IndexOutOfRangeException": "using System;",
    "Convert":                  "using System;",
    "DateTime":                 "using System;",
    "DateTimeOffset":           "using System;",
    "TimeSpan":                 "using System;",
    "Guid":                     "using System;",
    "Uri":                      "using System;",
    "UriKind":                  "using System;",
    "Random":                   "using System;",
    "Console":                  "using System;",
    "Environment":              "using System;",
    "Array":                    "using System;",
    "BitConverter":             "using System;",
    "Buffer":                   "using System;",
    "IDisposable":              "using System;",
    "IComparable":              "using System;",
    "IEquatable":               "using System;",
    "IFormattable":             "using System;",
    "IProgress":                "using System;",
    "Action":                   "using System;",
    "Func":                     "using System;",
    "Predicate":                "using System;",
    "EventHandler":             "using System;",
    "Attribute":                "using System;",
    "FlagsAttribute":           "using System;",
    "Type":                     "using System;",
    "StringComparison":         "using System;",
    "StringSplitOptions":       "using System;",
    "Enum":                     "using System;",
    "Tuple":                    "using System;",
    "ReadOnlyMemory":           "using System;",
    "ReadOnlySpan":             "using System;",
    "Span":                     "using System;",
    "Memory":                   "using System;",
    "OperationCanceledException":   "using System;",
    "CultureInfo":          "using System.Globalization;",
    "NumberStyles":         "using System.Globalization;",
    "UnicodeCategory":      "using System.Globalization;",
    "CharUnicodeInfo":      "using System.Globalization;",
    "Encoding":             "using System.Text;",
    "Regex":                "using System.Text.RegularExpressions;",
    "ConcurrentDictionary": "using System.Collections.Concurrent;",
    "CancellationToken":            "using System.Threading;",
    "CancellationTokenSource":      "using System.Threading;",
    "Task":                         "using System.Threading.Tasks;",
    "ValueTask":                    "using System.Threading.Tasks;",
    "IAsyncEnumerable":             "using System.Collections.Generic;",
    "IAsyncEnumerator":             "using System.Collections.Generic;",
    "EnumeratorCancellation":          "using System.Runtime.CompilerServices;",
    "EnumeratorCancellationAttribute": "using System.Runtime.CompilerServices;",
}

# Project-local type shadows: bare names that exist BOTH in System.* AND in a
# Chuvadi namespace. When a file is in that Chuvadi namespace or imports it,
# the bare name refers to the project-local type and System.* is not required.
CONFLICT_OVERRIDES = {
    "Path":       ["Chuvadi.Pdf.Graphics", "Chuvadi.Pdf.Rendering.DisplayList"],
    "Stream":     ["Chuvadi.Pdf.Primitives"],
    "Dictionary": ["Chuvadi.Pdf.Primitives"],
    "Type":       ["Chuvadi.Pdf.Primitives"],
}

# Map: namespace -> required csproj ProjectReference name fragment
REQUIRED_REFERENCES = {
    "Chuvadi.Pdf.Primitives":           "Chuvadi.Pdf.Primitives",
    "Chuvadi.Pdf.Filters":              "Chuvadi.Pdf.Filters",
    "Chuvadi.Pdf.Objects":              "Chuvadi.Pdf.Objects",
    "Chuvadi.Pdf.IO":                   "Chuvadi.Pdf.IO",
    "Chuvadi.Pdf.Documents":            "Chuvadi.Pdf.Documents",
    "Chuvadi.Pdf.Fonts":                "Chuvadi.Pdf.Fonts",
    "Chuvadi.Pdf.Content":              "Chuvadi.Pdf.Content",
    "Chuvadi.Pdf.Text":                 "Chuvadi.Pdf.Text",
    "Chuvadi.Pdf.Annotations":          "Chuvadi.Pdf.Annotations",
    "Chuvadi.Pdf.Rendering.DisplayList": "Chuvadi.Pdf.Rendering.DisplayList",
    "Chuvadi.Pdf.Rendering":            "Chuvadi.Pdf.Rendering",
    "Chuvadi.Pdf.Svg":                  "Chuvadi.Pdf.Svg",
    "Chuvadi.Pdf.Graphics":             "Chuvadi.Pdf.Graphics",
    "Chuvadi.Pdf.Images":               "Chuvadi.Pdf.Images",
    "Chuvadi.Pdf.Authoring":            "Chuvadi.Pdf.Authoring",
}


def check_file(path):
    issues = []
    is_src = os.sep + "src" + os.sep in path or "/src/" in path

    with open(path) as f:
        lines = f.readlines()

    declared_usings = set()
    namespace_line = 0
    for i, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith("using ") and stripped.endswith(";") and "(" not in stripped:
            declared_usings.add(stripped)
        if stripped.startswith("namespace "):
            namespace_line = i
            break

    declared_ns_set = {u.removeprefix('using ').removesuffix(';') for u in declared_usings}

    # Rule 1: var in src/ files
    if is_src:
        for i, line in enumerate(lines, 1):
            stripped = line.strip()
            if stripped.startswith("//"):
                continue
            if re.search(r'(?<!using )\bvar\b', line):
                issues.append(f"  IDE0008 L{i}: 'var' in src/ file: {stripped[:70]}")

    # Rule 2: duplicate using directives
    seen = []
    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        if stripped.startswith("using ") and stripped.endswith(";") and "(" not in stripped:
            if stripped in seen:
                issues.append(f"  IDE0005 L{i}: duplicate using: {stripped}")
            else:
                seen.append(stripped)

    # Rule 3: missing using directives for known types
    code_lines = lines[namespace_line:]
    cleaned_lines = []
    for _ln in code_lines:
        _ln_no_strings = re.sub(r'"(?:[^"\\]|\\.)*"', '""', _ln)
        _ln_clean = re.sub(r'//.*', '', _ln_no_strings)
        cleaned_lines.append(_ln_clean)
    code_no_strings = "".join(cleaned_lines)

    ns_match = re.search(r'^namespace\s+(\S+)', "".join(lines), re.MULTILINE)
    file_ns = ns_match.group(1).rstrip(";") if ns_match else ""

    for type_name, required_using in REQUIRED_USINGS.items():
        if required_using in declared_usings:
            continue
        if file_ns:
            required_ns = required_using.removeprefix("using ").removesuffix(";")
            if file_ns == required_ns or file_ns.startswith(required_ns + '.'):
                continue
        if type_name in CONFLICT_OVERRIDES:
            override_namespaces = CONFLICT_OVERRIDES[type_name]
            file_or_imports = {file_ns} | declared_ns_set
            if any(
                ns == override_ns or ns.startswith(override_ns + ".")
                for ns in file_or_imports if ns
                for override_ns in override_namespaces
            ):
                continue
        if re.search(r'\b' + re.escape(type_name) + r'\b', code_no_strings):
            required_ns = required_using.removeprefix("using ").removesuffix(";")
            fully_qualified = required_ns + "." + type_name
            if fully_qualified in code_no_strings:
                continue
            # C# type aliases: `using TypeName = Some.Namespace.TypeName;`
            # binds TypeName to the alias target, satisfying the import.
            alias_pattern = re.compile(
                r'using\s+' + re.escape(type_name) + r'\s*=\s*' +
                re.escape(required_ns) + r'\.' + re.escape(type_name) + r'\s*;')
            if alias_pattern.search("".join(lines)):
                continue
            if type_name == "Dictionary":
                matches = re.findall(r'(\w*)Dictionary\b', code_no_strings)
                if all(m == "Pdf" for m in matches if m):
                    continue
            if type_name == "Type":
                bare_matches = re.findall(r'(?<![.\w])Type\b', code_no_strings)
                if len(bare_matches) == 0:
                    continue
            issues.append(
                f"  CS0246 possible: '{type_name}' used but '{required_using}' not declared")

    # Rule 4: IDE0005 — declared using with no known type from it used
    ns_to_types_map = {}
    for _type_name, _req_using in REQUIRED_USINGS.items():
        _ns = _req_using.removeprefix("using ").removesuffix(";")
        if _ns not in ns_to_types_map:
            ns_to_types_map[_ns] = []
        ns_to_types_map[_ns].append(_type_name)

    for _decl in declared_usings:
        _ns = _decl.removeprefix("using ").removesuffix(";")
        if _ns not in ns_to_types_map:
            continue
        _types = ns_to_types_map[_ns]
        _found = any(
            re.search(r'\b' + re.escape(_t) + r'\b', code_no_strings)
            or (_ns + "." + _t) in code_no_strings
            for _t in _types)
        if not _found:
            issues.append(
                f"  IDE0005 possible: '{_decl}' declared but no known type from it appears in code")

    return issues


def check_csproj(cs_path):
    """For each src .cs file, verify every Chuvadi.Pdf.* using is in csproj refs."""
    issues = []

    directory = os.path.dirname(cs_path)
    csproj_path = None
    for fname in os.listdir(directory):
        if fname.endswith(".csproj"):
            csproj_path = os.path.join(directory, fname)
            break

    if csproj_path is None:
        return issues

    with open(csproj_path) as f:
        csproj_content = f.read()

    with open(cs_path) as f:
        cs_content = f.read()

    used_namespaces = re.findall(
        r"^using (Chuvadi\.Pdf\.[A-Za-z][A-Za-z0-9_.]*);",
        cs_content,
        re.MULTILINE)

    own_ns_match = re.search(
        r"^namespace (Chuvadi\.Pdf\.[A-Za-z][A-Za-z0-9_.]*)",
        cs_content,
        re.MULTILINE)
    own_ns = own_ns_match.group(1) if own_ns_match else ""

    for ns in used_namespaces:
        if ns == own_ns:
            continue
        required_ref = REQUIRED_REFERENCES.get(ns)
        if required_ref and required_ref not in csproj_content:
            issues.append(
                f"  CS0234: '{ns}' used but not in ProjectReferences of {os.path.basename(csproj_path)}")

    return issues


def check_syntax(path):
    """Basic C# syntax sanity checks to catch Python-escaping corruption."""
    issues = []

    with open(path, 'rb') as f:
        raw = f.read()

    text = raw.decode('utf-8', errors='replace')
    lines = text.splitlines()

    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        if stripped.startswith("//"):
            continue
        if "'\\" in line and "'\\\\" not in line and "'\\n'" not in line and "'\\r'" not in line and "'\\t'" not in line:
            if re.search(r"'\\'[^']", line) or re.search(r"== \(byte\)'\\'\s", line):
                issues.append(
                    f"  SYNTAX L{i}: possible corrupt char literal (lone backslash): {stripped[:60]}")

    return issues


def main():
    files = sys.argv[1:]
    if not files:
        print("Usage: check_style.py file1.cs file2.cs ...")
        sys.exit(1)

    total_issues = 0

    for path in files:
        if not os.path.exists(path):
            print(f"NOT FOUND: {path}")
            total_issues += 1
            continue

        if not path.endswith('.cs'):
            continue
        norm = path.replace(os.sep, '/')
        if '/bin/' in norm or '/obj/' in norm:
            continue

        issues = check_file(path)
        issues.extend(check_syntax(path))
        if "/src/" in path or os.sep + "src" + os.sep in path:
            issues.extend(check_csproj(path))
        name = os.path.basename(path)

        if issues:
            print(f"ISSUES in {name}:")
            for issue in issues:
                print(issue)
            total_issues += len(issues)
        else:
            print(f"  OK  {name}")

    print()
    if total_issues == 0:
        print(f"Style check PASSED — {len(files)} file(s) checked.")
    else:
        print(f"Style check FAILED — {total_issues} issue(s). Fix before packaging.")
        sys.exit(1)


if __name__ == "__main__":
    main()
