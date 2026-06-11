# ReportBuilder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Composes multi-page PDF reports from flowing content blocks — headings, paragraphs, bulleted and numbered lists, tables, images, rules, and page breaks — with automatic pagination.

```csharp
public sealed class ReportBuilder
```

## Remarks

Unlike `PdfDocumentBuilder`, which draws at explicit coordinates, `ReportBuilder` lays content out top-to-bottom inside the page margins and starts new pages as needed. Tables longer than a page continue across pages, optionally repeating their header row. Page headers and footers support `{page}`, `{total}`, `{title}`, and `{date}` tokens with Arabic, Roman, or letter page numbering.  

 Every styling knob has a default, so a minimal report is three lines: create, add content, save.

## Methods

### `Create`

__static__

```csharp
static ReportBuilder Create() => new()
```

Creates a new empty report.

### `SetTitle`

```csharp
ReportBuilder SetTitle(string title)
```

Sets the document /Title metadata (also available to headers/footers as {title}).

### `SetAuthor`

```csharp
ReportBuilder SetAuthor(string author)
```

Sets the document /Author metadata.

### `SetSubject`

```csharp
ReportBuilder SetSubject(string subject)
```

Sets the document /Subject metadata.

### `WithPageSetup`

```csharp
ReportBuilder WithPageSetup(ReportPageSetup setup)
```

Sets the page size and margins for every page.

### `WithHeader`

```csharp
ReportBuilder WithHeader(HeaderFooterStyle header)
```

Sets a styled page header. The text supports the tokens `{page}`, `{total}`, `{title}`, and `{date}`.

### `WithFooter`

```csharp
ReportBuilder WithFooter(HeaderFooterStyle footer)
```

Sets a styled page footer. The text supports the tokens `{page}`, `{total}`, `{title}`, and `{date}` — for example `"Page {page} of {total}"`.

### `WithHeader`

```csharp
ReportBuilder WithHeader(Action<PageBuilder, int, int> draw)
```

Sets a free-form header callback receiving the page, 1-based page number, and total page count — the escape hatch when the styled header is not flexible enough.

### `WithFooter`

```csharp
ReportBuilder WithFooter(Action<PageBuilder, int, int> draw)
```

Sets a free-form footer callback. Same shape as `WithHeader(Action{PageBuilder, int, int})`.

### `AddHeading`

```csharp
ReportBuilder AddHeading(string text, int level = 1)
```

Adds a heading. Levels 1–3 map to 16 / 13.5 / 12 point bold with matching spacing; other levels render as level 3.

### `AddHeading`

```csharp
ReportBuilder AddHeading(string text, ParagraphStyle style)
```

Adds a heading with a fully custom style.

### `AddParagraph`

```csharp
ReportBuilder AddParagraph(string text, ParagraphStyle? style = null)
```

Adds a paragraph using `ParagraphStyle.Default` or the supplied style.

### `AddBulletList`

```csharp
ReportBuilder AddBulletList(IEnumerable<string> items, ListStyle? style = null)
```

Adds a bulleted list.

### `AddNumberedList`

```csharp
ReportBuilder AddNumberedList(IEnumerable<string> items, ListStyle? style = null)
```

Adds a numbered list (Arabic, Roman, or letter numbering per the style).

### `AddTable`

```csharp
ReportBuilder AddTable(ReportTable table)
```

Adds a table; tables longer than a page paginate automatically.

### `AddSpacer`

```csharp
ReportBuilder AddSpacer(double points)
```

Adds vertical blank space.

### `AddPageBreak`

```csharp
ReportBuilder AddPageBreak()
```

Forces the following content onto a new page.

### `ToByteArray`

```csharp
byte[] ToByteArray()
```

Composes the report and returns the PDF bytes.

### `Save`

```csharp
void Save(Stream output)
```

Composes the report and writes the PDF to a stream.

### `SaveToFile`

```csharp
void SaveToFile(string path)
```

Composes the report and writes the PDF to a file (overwritten when present).

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
