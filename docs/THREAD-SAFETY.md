# Thread safety

This page states which Chuvadi objects may be used from several threads at
once. It is the contract from 3.31.2 (D-217 D). Anything not listed here
should be treated as not thread-safe.

## One `PdfDocument` per thread

A `PdfDocument` is **not** safe for concurrent use. It loads objects and pages
on demand into plain caches, and its reader shares one stream position. Two
threads that read, render or extract from the same document at the same time
can corrupt those caches or read the wrong bytes.

To work on one file from several threads, open one `PdfDocument` per thread.
The documents can be opened from the same bytes, for example one
`new MemoryStream(bytes, writable: false)` each. Everything a document hands
out (pages, display lists, annotations, outlines) belongs to that document and
follows the same rule.

Different documents on different threads are independent, and this is the
normal way to run Chuvadi in a server.

## Safe to share

These objects can be created once and used from any number of threads at the
same time:

- **`FontFace`.** It is read-only after `FontFace.Load`. Glyph bounds are
  computed on first use and kept in a concurrent cache.
- **`TextShaper`.** It is a static class. Its caches (the face loaded for each
  font array, and the shaping plans built for each face) are concurrent
  and keyed weakly, so they do not keep fonts alive.
- **`FontRenderer`, since 3.31.2.** Both glyph caches are concurrent, and
  hinting runs one glyph at a time per instance, because the hinting
  interpreter is reused between glyphs. Two threads asking for the same new
  glyph may both build it; the results are equal. Before 3.31.2 a shared
  `FontRenderer` could corrupt its caches or throw.
- **`PdfName`.** `PdfName.Intern` is safe from any thread. Since 3.31.2 names
  compare by value, and the shared name table holds at most the well-known
  names plus 4,096 others of up to 64 characters.

## Pass the same font array to `TextShaper.Shape`

`TextShaper.Shape(byte[] ttf, …)` keeps the face it loads for as long as that
array is alive, and finds it again by the array instance, not by its bytes.

`FontPack.ReadAllBytes` (in the Chuvadi.Fonts package) returns a **new** copy
of the file each time it is called. Code like this loads and parses the font
again on every call:

```csharp
// Slow: a new array, so a new face, every call.
TextShaper.Shape(FontPack.ReadAllBytes(fileName), text, script);
```

Read the bytes once and keep the array, or better, load a `FontFace` once and
use the `FontFace` overload:

```csharp
FontFace face = FontFace.Load(FontPack.ReadAllBytes(fileName)); // once
TextShaper.Shape(face, text, script);                            // any thread
```
