# Embedding Chuvadi to view untrusted PDFs

This page is for applications that open PDFs they did not create — a mail
client previewing attachments, a document viewer, a server-side thumbnailer.
It states what Chuvadi guarantees when it opens, renders and extracts text, and
how to bound the work. Everything here is available from 3.24.0.

## 1. What viewing never does

Opening a document (`PdfDocument.Open` / `OpenAsync`), rendering it
(`PageRasterizer`, `SvgRenderer`) and extracting its text (`TextExtractor`,
`DocumentSearch`) **never**:

- **runs scripts.** Document, page, field and annotation JavaScript, `/OpenAction`,
  additional actions (`/AA`) and XFA scripts are data to the viewing path;
  nothing interprets them. Annotations are drawn from their appearance streams
  only.
- **follows actions.** Launch, URI, GoToR/GoToE, SubmitForm, ImportData and
  media actions are never executed. Links are not followed.
- **opens embedded files.** Attachments, portfolios and associated files are
  never extracted or opened.
- **touches the network.** There is no HTTP, socket or DNS code on the viewing
  path. Fonts that are not embedded use Chuvadi's built-in Standard 14
  substitutes; no system or remote font lookup is made. External stream data,
  remote go-to targets and URL references are never fetched.
- **starts processes.**
- **writes to disk (A5).** Nothing is spilled to temporary files: decoding,
  rendering and extraction work in memory. The only file access is the
  read-only open of a path *you* pass to the path overloads of
  `PdfDocument.Open` / `OpenAsync` / `RequiresPassword`.

These are checked by `EmbeddingGuaranteeTests`, which scans the compiled
assemblies on the viewing path (Primitives, Filters, Objects, Encryption, IO,
Documents, Content, Fonts, Fonts.Rendering, Fonts.Woff2, Graphics, Images,
Rendering.Walking, Rendering.DisplayList, Rendering.Raster, Rendering, Text,
Text.Shaping, Svg) for any reference to network, process, temporary-file or
file-system APIs (other than that one read-only open), and checks that none of
them depends on `Chuvadi.Pdf.Xfa`, `Chuvadi.Pdf.Signatures` or
`Chuvadi.Cryptography`.

Outside the viewing path, by your explicit call only:

- `XfaRenderer` (in `Chuvadi.Pdf.Xfa`) runs XFA form scripts in Chuvadi's
  built-in FormCalc/JavaScript engines by default. Pass
  `ScriptMode.None` in `XfaRenderOptions` when rendering untrusted XFA forms.
- Signing (`Chuvadi.Pdf.Signatures`) contacts timestamp, OCSP and CRL servers
  through the `HttpClient` you supply.
- Saving APIs write to the paths you give them.

## 2. Disarming a file you pass on (A2)

To hand a PDF to *another* viewer safely (forwarding an attachment, archiving),
write a disarmed copy:

```csharp
using PdfDocument doc = PdfDocument.Open(input, new PdfOpenOptions());
SanitizeReport found = PdfSanitizer.Inspect(doc);   // what is active (changes nothing)
SanitizeReport removed = PdfSanitizer.Sanitize(doc, output); // write the disarmed copy
```

`Sanitize` removes `/OpenAction`, every `/AA`, the JavaScript name tree, every
action other than in-document navigation (GoTo, the four page-turning named
actions, threads, layer toggles, transitions, hide — `URI` links too unless
`KeepUriLinks`), embedded files (`/EmbeddedFiles`, `/AF`, `/EF`, `/RF`,
portfolios, file-attachment annotations), XFA packets and multimedia
annotations. Each category can be kept with `SanitizeOptions`. The copy is a
full rewrite: signatures no longer validate, and an encrypted input is only
accepted with `AllowEncryptedInput` (the copy is written unencrypted).

## 3. Passwords (A1)

```csharp
if (PdfDocument.RequiresPassword(stream)) { /* ask the user */ }
PdfDocument doc = PdfDocument.Open(stream, new PdfOpenOptions { Password = pw });
```

Opening an encrypted file that needs a password without one (or with a wrong
one) throws `PdfPasswordRequiredException` — a `PdfParseException`, so older
`catch` blocks still work. `PasswordSupplied` tells "ask for a password" from
"wrong password". Either the user or the owner password opens the file, for
every revision (RC4 R2/R3, AES-128 R4, AES-256 R6). `RequiresPassword` reads only the cross-reference data and
the encryption dictionary, and restores the stream position.

## 4. Cancellation (A3)

```csharp
using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
PixelBuffer page = rasterizer.Rasterize(doc.Pages[0], cts.Token);
byte[] png = rasterizer.RasterizeToPng(doc.Pages[0], cts.Token);
PixelBuffer part = rasterizer.RenderRegion(doc.Pages[0], region, 150, cts.Token);
string text = extractor.ExtractText(doc.Pages[0], cts.Token);
List<TextFragment> runs = extractor.ExtractFragments(doc.Pages[0], cts.Token);
```

A cancelled token throws `OperationCanceledException`. The work checks the
token at every content operator and every glyph shown, every 4,096 operand
tokens, every mebibyte of skipped blank space, every painted scanline and
image row, every mebibyte of decompressed output and every JPEG block row, so
it stops well within a second (tests require under 1.5 s from cancel to return, including test-host
overhead). For anything else — `SvgRenderer`, `DocumentSearch`, form
flattening — open a scope yourself:

```csharp
using (PdfWorkScope.Enter(limits, cts.Token))
{
    string svg = new SvgRenderer().RenderPage(doc, 0);
}
```

Known gap: decoding a single JBIG2 or CCITT image is not interrupted part-way;
it is checked when the image is finished.

## 5. Limits (A4)

```csharp
PdfLimits limits = new()
{
    MaxInputBytes = 50 << 20,          // checked at open
    MaxPageCount = 2000,               // checked at open
    MaxContentOperators = 2_000_000,   // page complexity, per render/extract call
    MaxDecodedStreamBytes = 256 << 20, // any one stream (decompression bombs)
    MaxMemoryBytes = 1L << 30,         // large buffers per render/extract call
    MaxOutputPixels = 40_000_000,      // output raster size
};
using PdfDocument doc = PdfDocument.Open(stream, new PdfOpenOptions { Limits = limits });
```

All limits are off by default. A document opened with limits keeps them
(`PdfDocument.Limits`), and every `PageRasterizer` / `TextExtractor` call on it
applies them — with or without a token. Exceeding one throws
`PdfLimitExceededException` with `LimitName`, `Limit` and `Actual`; nothing
partial is returned.

- `MaxContentOperators` counts every operator a call processes, including
  forms, patterns, Type 3 glyphs and annotation appearances, plus every glyph
  shown and every 4,096 operands that no operator consumes — the measure of
  page complexity. (Inline arrays longer than 65,536 elements are truncated:
  no real `TJ` or dash array comes near that.) Engineering drawings that take seconds to render have
  hundreds of thousands to millions of operators; pair the limit with a
  cancellation timeout.
- `MaxMemoryBytes` is an accounting ceiling over the large buffers one call
  allocates (decoded streams as they grow, decoded images, raster buffers,
  and an estimate of the display list), charged before or while they are
  allocated. It is not a cap on the process: object graphs and caches are not
  counted, so a call can allocate somewhat more than the ceiling before it
  trips. Leave headroom.
- `MaxOutputPixels` is checked before the output buffer is allocated.

## 6. Exceptions (malformed input)

Through the APIs above, malformed or hostile input produces only these:

| Exception | When |
|---|---|
| `PdfPasswordRequiredException` (a `PdfParseException`) | Encrypted, password needed or wrong |
| `PdfParseException` | The file cannot be parsed. Any failure at open outside this table is reported as one, with the original as `InnerException` |
| `PdfCorruptionException` | The file parses but is inconsistent. Any render/extract failure outside this table is reported as one, with the original as `InnerException` |
| `PdfEncryptionException` | Unsupported security handler |
| `PdfLimitExceededException` | A `PdfLimits` value was exceeded |
| other `PdfException` subtypes (`FilterException`, `FontException`, `ContentException`, `RenderingException`, ...) | Module-specific failures |
| `OperationCanceledException` | Your token was cancelled |

So `catch (PdfException)` plus `catch (OperationCanceledException)` covers
every outcome of bad input. Exceptions that signal a problem on *your* side
pass through unchanged: `ArgumentNullException` / `ArgumentException` for bad
arguments (null, a non-seekable stream to `Open`), `ObjectDisposedException`
for a disposed document, `IOException` from the stream or file you supplied,
and `OutOfMemoryException` (prevent it with `PdfLimits`). A damaged font or
glyph no longer fails the page: the bad glyphs draw nothing.

Structures that would exhaust the stack or loop forever are stopped first:
arrays and dictionaries nested more than 512 levels are refused as a
`PdfParseException`; a page that is its own ancestor, a function or colour
space that contains itself, and Type 4 programs nested without end are
detected and reported (or skipped) instead of crashing the process. A page
tree's `/Count` is not trusted for allocation.

The set covers the `PdfOpenOptions` overloads of `PdfDocument.Open` /
`OpenAsync`, and every `PageRasterizer` and `TextExtractor` method listed in
section 4 (with or without a token). It was checked by mutation fuzzing
(8,000 damaged variants of the test fixtures per run): no exception outside
the table.

## 7. Threads

Use a `PdfDocument` (and the rasterizers and extractors on it) from one
thread at a time; open the file once per thread for parallel rendering. A
`PdfWorkScope` flows with the execution context into `await` and `Task.Run`.
