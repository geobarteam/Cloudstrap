---
name: pdf
description: "Use when working with PDF files in Cloudstrap. Covers reading/extracting text from PDFs, creating new PDFs, merging or splitting PDFs, adding watermarks, and manipulating pages using PDFsharp. For complex styled report generation, QuestPDF may be used as a fallback. Use for: generating reports, processing uploaded PDFs, extracting data from PDF documents, merging or splitting documents."
metadata:
  argument-hint: "Describe the PDF task, e.g. 'extract text from uploaded invoice PDF' or 'generate a summary report as PDF'"
---

# PDF Processing (.NET — PDFsharp)

Primary library: **PDFsharp** (MIT). Use **QuestPDF** only when PDFsharp's low-level drawing API is impractical for complex layouts.

> **Picking a document format:** PDF for fixed-layout reports and archival, **`xlsx`** skill for tabular data or data analysis, **`pptx`** skill for slide decks / presentations. Choose by what the consumer will actually do with the file.

## Quick Decision

| Task | Approach |
|------|----------|
| **Create PDF with text, tables, images** | PDFsharp |
| **Merge / split / rotate pages** | PDFsharp |
| **Extract text from PDF** | PDFsharp |
| **Add watermarks / headers / footers** | PDFsharp |
| **Complex multi-page report with auto-pagination** | QuestPDF (fallback) |

---

## Setup

```bash
dotnet add package PDFsharp
```

---

## Create a PDF Document

```csharp
using PdfSharp.Pdf;
using PdfSharp.Drawing;

var document = new PdfDocument();
document.Info.Title = "Monthly Report";

var page = document.AddPage();
var gfx = XGraphics.FromPdfPage(page);

var titleFont = new XFont("Arial", 20, XFontStyleEx.Bold);
var bodyFont = new XFont("Arial", 12, XFontStyleEx.Regular);

gfx.DrawString("Monthly Report", titleFont, XBrushes.Black,
    new XRect(0, 40, page.Width, 30), XStringFormats.TopCenter);

gfx.DrawString("Generated on " + DateTime.Today.ToString("yyyy-MM-dd"),
    bodyFont, XBrushes.DarkGray,
    new XRect(40, 80, page.Width - 80, 20), XStringFormats.TopLeft);

document.Save("report.pdf");
```

## Draw a Table

```csharp
using PdfSharp.Pdf;
using PdfSharp.Drawing;

var document = new PdfDocument();
var page = document.AddPage();
var gfx = XGraphics.FromPdfPage(page);

var headerFont = new XFont("Arial", 10, XFontStyleEx.Bold);
var cellFont = new XFont("Arial", 10, XFontStyleEx.Regular);
var pen = new XPen(XColors.Black, 0.5);

string[] headers = ["Name", "Amount", "Status"];
double[] colWidths = [200, 100, 100];
double x = 40, y = 60, rowHeight = 20;

// Draw header row
for (int col = 0; col < headers.Length; col++)
{
    var rect = new XRect(x, y, colWidths[col], rowHeight);
    gfx.DrawRectangle(pen, XBrushes.LightGray, rect);
    gfx.DrawString(headers[col], headerFont, XBrushes.Black, rect, XStringFormats.Center);
    x += colWidths[col];
}

// Draw data rows
var data = new[] { ("Item A", "1,500", "Active"), ("Item B", "2,300", "Pending") };
foreach (var (name, amount, status) in data)
{
    y += rowHeight;
    x = 40;
    string[] cells = [name, amount, status];
    for (int col = 0; col < cells.Length; col++)
    {
        var rect = new XRect(x, y, colWidths[col], rowHeight);
        gfx.DrawRectangle(pen, rect);
        gfx.DrawString(cells[col], cellFont, XBrushes.Black, rect, XStringFormats.Center);
        x += colWidths[col];
    }
}

document.Save("table.pdf");
```

## Merge PDFs

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

var outputDocument = new PdfDocument();

foreach (var file in new[] { "doc1.pdf", "doc2.pdf", "doc3.pdf" })
{
    var inputDocument = PdfReader.Open(file, PdfDocumentOpenMode.Import);
    foreach (var page in inputDocument.Pages)
    {
        outputDocument.AddPage(page);
    }
}

outputDocument.Save("merged.pdf");
```

## Split PDF (Extract Pages)

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

var source = PdfReader.Open("input.pdf", PdfDocumentOpenMode.Import);

// Extract pages 1-3 into a new document
var target = new PdfDocument();
for (int i = 0; i < 3 && i < source.PageCount; i++)
{
    target.AddPage(source.Pages[i]);
}

target.Save("first3pages.pdf");
```

## Rotate Pages

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

var document = PdfReader.Open("input.pdf", PdfDocumentOpenMode.Modify);

document.Pages[0].Rotate = (document.Pages[0].Rotate + 90) % 360;

document.Save("rotated.pdf");
```

## Add Watermark

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

var document = PdfReader.Open("input.pdf", PdfDocumentOpenMode.Modify);
var watermarkFont = new XFont("Arial", 60, XFontStyleEx.Bold);

foreach (var page in document.Pages)
{
    var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
    var size = page.Size;
    gfx.TranslateTransform(page.Width / 2, page.Height / 2);
    gfx.RotateTransform(-45);
    gfx.DrawString("CONFIDENTIAL", watermarkFont,
        new XSolidBrush(XColor.FromArgb(50, 255, 0, 0)),
        new XRect(-200, -30, 400, 60), XStringFormats.Center);
}

document.Save("watermarked.pdf");
```

## Extract Text

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

var document = PdfReader.Open("input.pdf", PdfDocumentOpenMode.ReadOnly);
foreach (var page in document.Pages)
{
    var content = ContentReader.ReadContent(page);
    ExtractText(content);
}

void ExtractText(CSequence sequence)
{
    foreach (var item in sequence)
    {
        if (item is COperator op && op.OpCode.Name == "Tj")
        {
            foreach (var operand in op.Operands.OfType<CString>())
            {
                Console.Write(operand.Value);
            }
        }
        else if (item is CSequence nested)
        {
            ExtractText(nested);
        }
    }
}
```

> **Note**: PDFsharp text extraction is basic — it reads text operators from the content stream. For PDFs with complex encodings or scanned images, consider a dedicated OCR tool.

## Add Header / Footer to Every Page

```csharp
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

var document = PdfReader.Open("input.pdf", PdfDocumentOpenMode.Modify);
var font = new XFont("Arial", 9, XFontStyleEx.Regular);

for (int i = 0; i < document.PageCount; i++)
{
    var page = document.Pages[i];
    var gfx = XGraphics.FromPdfPage(page);

    // Header
    gfx.DrawString("Confidential", font, XBrushes.Gray,
        new XRect(40, 15, page.Width - 80, 15), XStringFormats.TopLeft);

    // Footer with page number
    gfx.DrawString($"Page {i + 1} of {document.PageCount}",
        font, XBrushes.Gray,
        new XRect(40, page.Height - 30, page.Width - 80, 15), XStringFormats.TopRight);
}

document.Save("with_headers.pdf");
```

---

## Common Patterns

### Reading PDF in a Controller (file upload)

```csharp
[HttpPost("upload-pdf")]
public async Task<IActionResult> UploadPdf(IFormFile file, CancellationToken ct)
{
    if (file.ContentType != "application/pdf")
        return BadRequest("Only PDF files are accepted.");

    using var stream = file.OpenReadStream();
    var result = await _pdfHandler.ProcessAsync(stream, ct);
    if (!result.IsSuccess)
        return BadRequest(result.Error);

    return Ok(result.Value);
}
```

### Generate PDF as Download

```csharp
[HttpGet("report")]
public IActionResult GenerateReport()
{
    var document = new PdfDocument();
    // ... build document ...

    using var stream = new MemoryStream();
    document.Save(stream, false);

    return File(stream.ToArray(), "application/pdf", "report.pdf");
}
```

---

## QuestPDF (Fallback — Complex Layouts Only)

Use QuestPDF only when PDFsharp's manual drawing is impractical (e.g., multi-page reports with auto-pagination, dynamic tables that span pages).

```bash
dotnet add package QuestPDF
```

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        page.Header().Text("Report").FontSize(20).Bold();
        page.Content().Column(col =>
        {
            col.Item().Text("Auto-paginated content...");
        });
        page.Footer().AlignCenter().Text(x =>
        {
            x.Span("Page ");
            x.CurrentPageNumber();
        });
    });
}).GeneratePdf("report.pdf");
```

## Dependencies

| Package | Install | Use case |
|---------|---------|----------|
| PDFsharp | `dotnet add package PDFsharp` | Create, merge, split, manipulate, extract |
| QuestPDF | `dotnet add package QuestPDF` | Complex auto-paginated reports (fallback) |
