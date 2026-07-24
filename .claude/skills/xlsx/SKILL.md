---
name: xlsx
description: "Use when working with Excel spreadsheet files (.xlsx, .csv, .tsv) in Cloudstrap. Covers reading, creating, editing, and analyzing spreadsheet data. Supports .NET libraries (ClosedXML, EPPlus, DocumentFormat.OpenXml) for production code and Python (openpyxl, pandas) for quick scripting. Use for: generating Excel reports, importing/exporting data, processing uploaded spreadsheets, data analysis, bulk data operations."
metadata:
  argument-hint: "Describe the spreadsheet task, e.g. 'generate monthly report as xlsx' or 'import data from uploaded Excel file'"
---

# XLSX Processing

> **Picking a document format:** XLSX for tabular data or data analysis, **`pdf`** skill for fixed-layout archival reports, **`pptx`** skill for slide decks. Choose by what the consumer will actually do with the file.

## Quick Decision

| Task | Best approach |
|------|---------------|
| **Generate styled reports** | ClosedXML (.NET) or openpyxl (Python) |
| **Data analysis / pivot** | pandas (Python) |
| **Import/export in production** | ClosedXML or EPPlus (.NET) |
| **Read large files** | EPPlus streaming or pandas chunked |
| **Quick one-off processing** | Python script (fastest to write) |
| **Template-based reports** | ClosedXML with template file (.NET) |

---

## .NET Libraries

### ClosedXML — Create and Edit Excel Files

NuGet: `ClosedXML` (MIT).

```csharp
using ClosedXML.Excel;

// Create a new workbook
using var workbook = new XLWorkbook();
var sheet = workbook.Worksheets.Add("Report");

// Headers
sheet.Cell("A1").Value = "Name";
sheet.Cell("B1").Value = "Amount";
sheet.Cell("C1").Value = "Date";
sheet.Row(1).Style.Font.Bold = true;

// Data
sheet.Cell("A2").Value = "Item One";
sheet.Cell("B2").Value = 1500.50;
sheet.Cell("C2").Value = DateTime.Today;

// Formatting
sheet.Cell("B2").Style.NumberFormat.Format = "#,##0.00";
sheet.Cell("C2").Style.DateFormat.Format = "yyyy-MM-dd";

// Auto-fit columns
sheet.Columns().AdjustToContents();

// Formulas — always use Excel formulas, never hardcode calculated values
sheet.Cell("B10").FormulaA1 = "=SUM(B2:B9)";

workbook.SaveAs("report.xlsx");
```

### ClosedXML — Read Excel Files

```csharp
using ClosedXML.Excel;

using var workbook = new XLWorkbook("input.xlsx");
var sheet = workbook.Worksheet(1); // or by name: workbook.Worksheet("Sheet1")

// Read all rows
var rows = sheet.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>();
foreach (var row in rows.Skip(1)) // skip header
{
    var name = row.Cell(1).GetString();
    var amount = row.Cell(2).GetDouble();
    var date = row.Cell(3).GetDateTime();
}
```

### EPPlus — Advanced Features

NuGet: `EPPlus` (Polyform Noncommercial or commercial license).

```csharp
using OfficeOpenXml;

ExcelPackage.License.SetNonCommercialPersonal("your-name");

using var package = new ExcelPackage();
var sheet = package.Workbook.Worksheets.Add("Data");

// Load from collection
var data = new[] { new { Name = "A", Value = 1 }, new { Name = "B", Value = 2 } };
sheet.Cells["A1"].LoadFromCollection(data, PrintHeaders: true);

// Add chart
var chart = sheet.Drawings.AddChart("Chart1", OfficeOpenXml.Drawing.Chart.eChartType.ColumnClustered);
chart.SetPosition(5, 0, 0, 0);
chart.SetSize(600, 300);
var series = chart.Series.Add(sheet.Cells["B2:B3"], sheet.Cells["A2:A3"]);

await package.SaveAsAsync(new FileInfo("output.xlsx"));
```

---

## Python Scripts (Quick Processing)

### Read and Analyze with pandas

```python
# pip install pandas openpyxl
import pandas as pd

df = pd.read_excel("input.xlsx")
print(df.describe())
print(df.head())

# Filter and export
filtered = df[df["Amount"] > 100]
filtered.to_excel("filtered.xlsx", index=False)
```

### Create with openpyxl

```python
# pip install openpyxl
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill

wb = Workbook()
ws = wb.active
ws.title = "Report"

# Header row
headers = ["Name", "Amount", "Status"]
for col, header in enumerate(headers, 1):
    cell = ws.cell(row=1, column=col, value=header)
    cell.font = Font(bold=True)
    cell.fill = PatternFill("solid", fgColor="4472C4")

# Use Excel formulas, not Python calculations
ws["B10"] = "=SUM(B2:B9)"

ws.column_dimensions["A"].width = 20
wb.save("output.xlsx")
```

---

## Common Patterns

### Import Excel in a Controller (file upload)

```csharp
[HttpPost("import")]
public async Task<IActionResult> ImportExcel(IFormFile file, CancellationToken ct)
{
    if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        return BadRequest("Only .xlsx files are accepted.");

    using var stream = file.OpenReadStream();
    using var workbook = new XLWorkbook(stream);
    var sheet = workbook.Worksheet(1);

    var result = await _importHandler.ProcessAsync(sheet, ct);
    if (!result.IsSuccess)
        return BadRequest(result.Error);

    return Ok(result.Value);
}
```

### Export Data as Excel Download

```csharp
[HttpGet("export")]
public async Task<IActionResult> ExportExcel(CancellationToken ct)
{
    var data = await _query.Execute(ct);

    using var workbook = new XLWorkbook();
    var sheet = workbook.Worksheets.Add("Export");
    // ... populate sheet ...

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    stream.Position = 0;

    return File(
        stream.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "export.xlsx");
}
```

## Key Rules

- **Use Excel formulas** — never calculate in code and hardcode the result. The spreadsheet must stay dynamic.
- **Use `using`** — always dispose workbook/package objects.
- **Validate uploads** — check file extension and content type at the controller boundary.

## Dependencies

| Library | NuGet / pip | Use case |
|---------|-------------|----------|
| ClosedXML | `dotnet add package ClosedXML` | Create, read, edit (MIT) |
| EPPlus | `dotnet add package EPPlus` | Charts, pivot, advanced |
| openpyxl | `pip install openpyxl` | Python: create/edit Excel |
| pandas | `pip install pandas openpyxl` | Python: data analysis |
