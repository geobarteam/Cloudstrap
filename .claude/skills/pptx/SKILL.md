---
name: pptx
description: "Use when working with PowerPoint files (.pptx) in Cloudstrap. Covers creating presentations from scratch, reading/extracting content from existing decks, editing slides, and adding charts or images. Supports .NET (DocumentFormat.OpenXml, Syncfusion) and Python (python-pptx) approaches. Use for: generating slide decks, reading presentation content, creating reports as PowerPoint, automated deck generation."
metadata:
  argument-hint: "Describe the presentation task, e.g. 'create a summary deck from quarterly data' or 'extract text from uploaded pptx'"
---

# PPTX Processing

> **Picking a document format:** PPTX for slide decks and presentations, **`pdf`** skill for fixed-layout archival reports, **`xlsx`** skill for tabular data. Choose by what the consumer will actually do with the file.

## Quick Decision

| Task | Best approach |
|------|---------------|
| **Create deck from scratch** | python-pptx (Python) — fastest to write |
| **Read / extract content** | python-pptx or markitdown (Python) |
| **Production .NET generation** | DocumentFormat.OpenXml or Syncfusion |
| **Template-based decks** | python-pptx with template file |
| **Quick one-off** | Python script |

---

## Python — python-pptx (Recommended for Most Tasks)

python-pptx is the most practical tool for creating and editing PowerPoint files.

### Create a Presentation

```python
# pip install python-pptx
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.enum.text import PP_ALIGN

prs = Presentation()

# Title slide
slide = prs.slides.add_slide(prs.slide_layouts[0])
slide.shapes.title.text = "Monthly Report"
slide.placeholders[1].text = "Generated automatically"

# Content slide with bullet points
slide = prs.slides.add_slide(prs.slide_layouts[1])
slide.shapes.title.text = "Key Findings"
body = slide.placeholders[1]
body.text = "Revenue increased 15%"
body.text_frame.add_paragraph().text = "Customer base grew by 2,000"
body.text_frame.add_paragraph().text = "Operating costs reduced 8%"

# Blank slide with custom content
slide = prs.slides.add_slide(prs.slide_layouts[6])
txBox = slide.shapes.add_textbox(Inches(1), Inches(1), Inches(8), Inches(1))
tf = txBox.text_frame
p = tf.paragraphs[0]
p.text = "Custom positioned text"
p.font.size = Pt(24)
p.font.bold = True
p.alignment = PP_ALIGN.CENTER

prs.save("report.pptx")
```

### Read a Presentation

```python
from pptx import Presentation

prs = Presentation("input.pptx")
for i, slide in enumerate(prs.slides, 1):
    print(f"--- Slide {i} ---")
    for shape in slide.shapes:
        if shape.has_text_frame:
            for paragraph in shape.text_frame.paragraphs:
                print(paragraph.text)
```

### Add a Table

```python
from pptx import Presentation
from pptx.util import Inches

prs = Presentation()
slide = prs.slides.add_slide(prs.slide_layouts[6])

rows, cols = 4, 3
table = slide.shapes.add_table(rows, cols, Inches(1), Inches(1.5), Inches(8), Inches(3)).table

# Set column widths
table.columns[0].width = Inches(3)
table.columns[1].width = Inches(2.5)
table.columns[2].width = Inches(2.5)

# Header row
for j, header in enumerate(["Name", "Value", "Status"]):
    table.cell(0, j).text = header

# Data rows
data = [("Item A", "1,500", "Active"), ("Item B", "2,300", "Pending")]
for i, row_data in enumerate(data, 1):
    for j, val in enumerate(row_data):
        table.cell(i, j).text = val

prs.save("with_table.pptx")
```

### Add a Chart

```python
from pptx import Presentation
from pptx.chart.data import CategoryChartData
from pptx.enum.chart import XL_CHART_TYPE
from pptx.util import Inches

prs = Presentation()
slide = prs.slides.add_slide(prs.slide_layouts[6])

chart_data = CategoryChartData()
chart_data.categories = ["Q1", "Q2", "Q3", "Q4"]
chart_data.add_series("Revenue", (120, 135, 150, 180))
chart_data.add_series("Costs", (90, 95, 100, 105))

chart = slide.shapes.add_chart(
    XL_CHART_TYPE.COLUMN_CLUSTERED,
    Inches(1), Inches(1.5), Inches(8), Inches(4.5),
    chart_data
).chart
chart.has_legend = True

prs.save("with_chart.pptx")
```

### Edit from Template

```python
from pptx import Presentation

prs = Presentation("template.pptx")

# Replace placeholder text
for slide in prs.slides:
    for shape in slide.shapes:
        if shape.has_text_frame:
            for paragraph in shape.text_frame.paragraphs:
                if "{{TITLE}}" in paragraph.text:
                    paragraph.text = paragraph.text.replace("{{TITLE}}", "Actual Title")

prs.save("filled.pptx")
```

---

## .NET — DocumentFormat.OpenXml

NuGet: `DocumentFormat.OpenXml` (MIT). Low-level but full control.

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using D = DocumentFormat.OpenXml.Drawing;

// Read text from all slides
using var doc = PresentationDocument.Open("input.pptx", false);
var presentationPart = doc.PresentationPart!;

foreach (var slidePart in presentationPart.SlideParts)
{
    var texts = slidePart.Slide.Descendants<D.Text>();
    foreach (var text in texts)
    {
        Console.WriteLine(text.Text);
    }
}
```

> **Note**: Creating slides from scratch with OpenXml is verbose. Prefer python-pptx for generation tasks and OpenXml for reading or targeted edits in production .NET code.

---

## Quick Text Extraction

```bash
# pip install "markitdown[pptx]"
python -m markitdown presentation.pptx
```

---

## Design Tips

- **One idea per slide** — avoid walls of text.
- **36pt+ titles**, 14-16pt body text.
- **Use consistent color palette** — pick 2-3 colors and stick to them.
- **Every slide needs a visual** — table, chart, image, or diagram. Avoid text-only slides.
- **Left-align body text** — center only titles.

## Dependencies

| Library | Install | Use case |
|---------|---------|----------|
| python-pptx | `pip install python-pptx` | Create, edit, read (Python) |
| markitdown | `pip install "markitdown[pptx]"` | Quick text extraction |
| DocumentFormat.OpenXml | `dotnet add package DocumentFormat.OpenXml` | .NET read/edit |
