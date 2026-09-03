using ClosedXML.Excel;
using Microsoft.JSInterop;
using STTproject.Features.User.SalesInvoice.DTOs;

namespace STTproject.Features.User.SalesInvoice.Services
{
    public class DownloadTemplateService
    {
        private readonly IJSRuntime _jsRuntime;

        public DownloadTemplateService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task GenerateAndDownloadExcelAsync(
            List<(string Code, string Name)>? customers = null,
            List<(string Code, string Name)>? skus = null,
            List<string>? uoms = null,
            List<(string SkuCode, string Uom, decimal Price)>? prices = null)
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template");
                worksheet.Protect("!@#adhid");

                var customerTable = BuildHiddenLookupSheet(workbook, "CustomerList", customers);
                var skuTable = BuildHiddenLookupSheet(workbook, "SkuList", skus);
                var uomListRange = BuildHiddenListSheet(workbook, "UOMList", uoms);
                var priceTable = BuildHiddenPriceSheet(workbook, "PriceList", prices);

                if (uomListRange != null && !workbook.NamedRanges.Any(n => n.Name.Equals("AllUomList", StringComparison.OrdinalIgnoreCase)))
                {
                    workbook.NamedRanges.Add("AllUomList", uomListRange);
                }

                // Per-SKU UOM named ranges — indexed by position in `skus` (SKU_1, SKU_2, ...),
                // NOT by SKU text. Building names from SKU text requires sanitizing every possible
                // punctuation character Excel disallows in a defined name; miss even one and Excel
                // silently strips the name on open, which is exactly what caused the repair dialog.
                BuildSkuUomNamedRanges(workbook, skus, prices);

                // OrderType list (hidden sheet) to back the dropdown reliably
                var orderTypes = new[] { "Invoice", "Credit" };
                var orderTypeSheet = workbook.Worksheets.Add("OrderTypeList");
                orderTypeSheet.Visibility = XLWorksheetVisibility.Hidden;
                for (int i = 0; i < orderTypes.Length; i++)
                {
                    orderTypeSheet.Cell(i + 1, 1).Value = orderTypes[i];
                }
                var orderTypeSourceRange = orderTypeSheet.Range(1, 1, orderTypes.Length, 1);

                // ── Headers (11 columns) ──
                worksheet.Cell(1, 1).Value = "InvoiceCode";
                worksheet.Cell(1, 2).Value = "InvoiceDate";
                worksheet.Cell(1, 3).Value = "CustomerCode";
                worksheet.Cell(1, 4).Value = "CustomerName";
                worksheet.Cell(1, 5).Value = "OrderType";
                worksheet.Cell(1, 6).Value = "SalesManName";
                worksheet.Cell(1, 7).Value = "SkuCode";
                worksheet.Cell(1, 8).Value = "ItemName";
                worksheet.Cell(1, 9).Value = "UOM";
                worksheet.Cell(1, 10).Value = "Quantity";
                worksheet.Cell(1, 11).Value = "Amount";
                worksheet.Row(1).Style.Protection.Locked = true;

                worksheet.Columns(1, 11).Style.Protection.Locked = false;
                worksheet.Columns(12, 30).Style.Protection.Locked = false;
                worksheet.Range("A1:K1").Style.Protection.Locked = true;
                worksheet.SheetView.FreezeRows(1);

                worksheet.Column(1).Style.NumberFormat.Format = "@"; // InvoiceCode
                worksheet.Column(3).Style.NumberFormat.Format = "@"; // CustomerCode
                worksheet.Column(5).Style.NumberFormat.Format = "@"; // OrderType
                worksheet.Column(7).Style.NumberFormat.Format = "@"; // SkuCode
                worksheet.Column(9).Style.NumberFormat.Format = "@"; // UOM

                const int maxRow = 1048576;
                const int formulaFillRows = 2000;

                // ── InvoiceDate validation (B) ──
                var dateRange = worksheet.Range($"B2:B{maxRow}");
                dateRange.Style.DateFormat.Format = "dd/MM/yyyy";
                var dateValidation = dateRange.CreateDataValidation();
                dateValidation.Date.Between(new DateTime(2000, 1, 1), new DateTime(2100, 12, 31));
                dateValidation.IgnoreBlanks = true;
                dateValidation.ShowInputMessage = true;
                dateValidation.InputTitle = "Invoice Date";
                dateValidation.InputMessage = "Enter date in format: dd/MM/yyyy (e.g., 15/05/2026)";
                dateValidation.ShowErrorMessage = true;
                dateValidation.ErrorTitle = "Invalid Date";
                dateValidation.ErrorMessage = "Please enter a valid date.";

                // ── CustomerCode dropdown (C) ──
                if (customerTable != null)
                {
                    var customerValidation = worksheet.Range($"C2:C{maxRow}").CreateDataValidation();
                    customerValidation.List(customerTable.Value.CodeColumn);
                    customerValidation.InCellDropdown = true;
                    customerValidation.IgnoreBlanks = true;
                    customerValidation.ShowInputMessage = true;
                    customerValidation.InputTitle = "Customer Code";
                    customerValidation.InputMessage = "Select from dropdown.";
                    customerValidation.ShowErrorMessage = true;
                    customerValidation.ErrorStyle = XLErrorStyle.Stop;
                    customerValidation.ErrorTitle = "Invalid Customer Code";
                    customerValidation.ErrorMessage = "Please select a valid Customer Code from the dropdown.";
                }

                // ── CustomerName auto-fill (D) — locked formula ──
                if (customerTable != null)
                {
                    for (int row = 2; row <= formulaFillRows; row++)
                    {
                        worksheet.Cell(row, 4).FormulaA1 =
                            $"=IFERROR(VLOOKUP(C{row},CustomerList!A:B,2,FALSE),\"\")";
                    }
                }
                worksheet.Column(4).Style.Protection.Locked = true;

                // ── OrderType dropdown (E) ──
                var orderTypeRange = worksheet.Range($"E2:E{maxRow}").CreateDataValidation();
                orderTypeRange.List(orderTypeSourceRange);
                orderTypeRange.InCellDropdown = true;
                orderTypeRange.IgnoreBlanks = true;
                orderTypeRange.InputMessage = "Select from dropdown.";
                orderTypeRange.ShowInputMessage = true;
                orderTypeRange.InputTitle = "Order Type";
                orderTypeRange.ShowErrorMessage = true;
                orderTypeRange.ErrorStyle = XLErrorStyle.Stop;
                orderTypeRange.ErrorTitle = "Invalid Order Type";
                orderTypeRange.ErrorMessage = "Please select either Invoice or Credit";

                // ── SkuCode dropdown (G) ──
                if (skuTable != null)
                {
                    var skuValidation = worksheet.Range($"G2:G{maxRow}").CreateDataValidation();
                    skuValidation.List(skuTable.Value.CodeColumn);
                    skuValidation.InCellDropdown = true;
                    skuValidation.IgnoreBlanks = true;
                    skuValidation.ShowInputMessage = true;
                    skuValidation.InputTitle = "SKU Code";
                    skuValidation.InputMessage = "Select from dropdown.";
                    skuValidation.ShowErrorMessage = true;
                    skuValidation.ErrorStyle = XLErrorStyle.Stop;
                    skuValidation.ErrorTitle = "Invalid SKU Code";
                    skuValidation.ErrorMessage = "Please select a valid SKU Code from the dropdown.";
                }

                // ── ItemName auto-fill (H) — locked formula ──
                if (skuTable != null)
                {
                    for (int row = 2; row <= formulaFillRows; row++)
                    {
                        worksheet.Cell(row, 8).FormulaA1 =
                            $"=IFERROR(VLOOKUP(G{row},SkuList!A:B,2,FALSE),\"\")";
                    }
                }
                worksheet.Column(8).Style.Protection.Locked = true;

                // ── UOM dropdown (I) — cascades off the SkuCode in column G ──
                // Finds the SKU's position in SkuList via MATCH, then jumps to the named range
                // SKU_<that position>. Numeric-suffix names sidestep every Excel defined-name
                // character restriction entirely — no punctuation sanitizing needed anywhere.
                if (uomListRange != null && skuTable != null)
                {
                    var uomValidation = worksheet.Range($"I2:I{maxRow}").CreateDataValidation();
                    var uomFormula = "=INDIRECT(\"SKU_\"&MATCH(G2,SkuList!A:A,0))";
                    uomValidation.List(uomFormula, true);
                    uomValidation.InCellDropdown = true;
                    uomValidation.IgnoreBlanks = true;
                    uomValidation.ShowInputMessage = true;
                    uomValidation.InputTitle = "Unit of Measure (UOM)";
                    uomValidation.InputMessage = "Select the SKU first — the UOM list narrows to that item.";
                    uomValidation.ShowErrorMessage = true;
                    uomValidation.ErrorStyle = XLErrorStyle.Stop;
                    uomValidation.ErrorTitle = "Invalid UOM";
                    uomValidation.ErrorMessage = "Please select a valid UOM from the dropdown.";
                }
                else if (uomListRange != null)
                {
                    // unchanged flat fallback for when there's no SKU list at all
                    var uomValidation = worksheet.Range($"I2:I{maxRow}").CreateDataValidation();
                    uomValidation.List(uomListRange);
                    uomValidation.InCellDropdown = true;
                    uomValidation.IgnoreBlanks = true;
                    uomValidation.ShowInputMessage = true;
                    uomValidation.InputTitle = "Unit of Measure (UOM)";
                    uomValidation.InputMessage = "Select from dropdown.";
                    uomValidation.ShowErrorMessage = true;
                    uomValidation.ErrorStyle = XLErrorStyle.Stop;
                    uomValidation.ErrorTitle = "Invalid UOM";
                    uomValidation.ErrorMessage = "Please select a valid UOM from the dropdown.";
                }

                // ── Quantity validation (J) ──
                var qtyValidation = worksheet.Range($"J2:J{maxRow}").CreateDataValidation();
                qtyValidation.WholeNumber.GreaterThan(0);
                qtyValidation.IgnoreBlanks = true;
                qtyValidation.ShowInputMessage = true;
                qtyValidation.InputTitle = "Quantity";
                qtyValidation.InputMessage = "Must be a whole number greater than 0.";
                qtyValidation.ShowErrorMessage = true;
                qtyValidation.ErrorTitle = "Invalid Quantity";
                qtyValidation.ErrorMessage = "Quantity must be a whole number greater than 0";

                // ── Amount auto-calc (K) — locked formula: Quantity * price looked up by SkuCode+UOM ──
                if (priceTable != null)
                {
                    for (int row = 2; row <= formulaFillRows; row++)
                    {
                        worksheet.Cell(row, 11).FormulaA1 =
                            $"=IFERROR(J{row}*VLOOKUP(G{row}&\"|\"&I{row},PriceList!A:B,2,FALSE),0)";
                    }
                }
                worksheet.Column(11).Style.Protection.Locked = true;

                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                worksheet.Column(1).Width = 16;  // InvoiceCode
                worksheet.Column(2).Width = 14;  // InvoiceDate
                worksheet.Column(3).Width = CalcColumnWidth(customers?.Select(c => c.Code), "CustomerCode");
                worksheet.Column(4).Width = CalcColumnWidth(customers?.Select(c => c.Name), "CustomerName", 14, 45);
                worksheet.Column(5).Width = 12;  // OrderType
                worksheet.Column(6).Width = 20;  // SalesManName
                worksheet.Column(7).Width = CalcColumnWidth(skus?.Select(s => s.Code), "SkuCode");
                worksheet.Column(8).Width = CalcColumnWidth(skus?.Select(s => s.Name), "ItemName", 14, 45);
                worksheet.Column(9).Width = CalcColumnWidth(uoms, "UOM", 10, 20);
                worksheet.Column(10).Width = 12; // Quantity
                worksheet.Column(11).Width = 14; // Amount

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    stream.Position = 0;
                    var fileName = $"SalesInvoiceTemplate_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    await _jsRuntime.InvokeVoidAsync("downloadFile", stream.ToArray(), fileName,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                }
            }
        }

        private static double CalcColumnWidth(IEnumerable<string?>? values, string header, double min = 10, double max = 60)
        {
            double longest = header.Length;
            if (values != null)
            {
                foreach (var v in values)
                {
                    if (!string.IsNullOrEmpty(v) && v.Length > longest)
                    {
                        longest = v.Length;
                    }
                }
            }

            return Math.Clamp(longest + 2, min, max);
        }

        private static (IXLRange CodeColumn, IXLRange FullTable)? BuildHiddenLookupSheet(
            XLWorkbook workbook, string sheetName, List<(string Code, string Name)>? rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            var sheet = workbook.Worksheets.Add(sheetName);
            sheet.Visibility = XLWorksheetVisibility.Hidden;

            for (int i = 0; i < rows.Count; i++)
            {
                sheet.Cell(i + 1, 1).Value = rows[i].Code;
                sheet.Cell(i + 1, 2).Value = rows[i].Name;
            }

            var codeColumn = sheet.Range(1, 1, rows.Count, 1);
            var fullTable = sheet.Range(1, 1, rows.Count, 2);
            return (codeColumn, fullTable);
        }

        private static IXLRange? BuildHiddenPriceSheet(
            XLWorkbook workbook, string sheetName, List<(string SkuCode, string Uom, decimal Price)>? rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            var sheet = workbook.Worksheets.Add(sheetName);
            sheet.Visibility = XLWorksheetVisibility.Hidden;

            for (int i = 0; i < rows.Count; i++)
            {
                sheet.Cell(i + 1, 1).Value = $"{rows[i].SkuCode}|{rows[i].Uom}";
                sheet.Cell(i + 1, 2).Value = rows[i].Price;
            }

            return sheet.Range(1, 1, rows.Count, 2);
        }

        private static IXLRange? BuildHiddenListSheet(XLWorkbook workbook, string sheetName, List<string>? codes)
        {
            if (codes == null || codes.Count == 0)
            {
                return null;
            }

            var sheet = workbook.Worksheets.Add(sheetName);
            sheet.Visibility = XLWorksheetVisibility.Hidden;

            for (int i = 0; i < codes.Count; i++)
            {
                sheet.Cell(i + 1, 1).Value = codes[i];
            }

            return sheet.Range(1, 1, codes.Count, 1);
        }

        /// <summary>
        /// Writes one row per SKU (matching the SAME order as `skus`/SkuList, so MATCH position
        /// lines up) into a hidden sheet, and defines a named range SKU_&lt;1-based position&gt;
        /// for each SKU that has at least one UOM. SKUs with no UOM data get no named range —
        /// their row's UOM dropdown falls back to the full list via the formula's IFERROR.
        /// </summary>
        private static void BuildSkuUomNamedRanges(
            XLWorkbook workbook,
            List<(string Code, string Name)>? skus,
            List<(string SkuCode, string Uom, decimal Price)>? prices)
        {
            if (skus == null || skus.Count == 0 || prices == null || prices.Count == 0)
            {
                return;
            }

            var uomsBySku = prices
                .Where(p => !string.IsNullOrWhiteSpace(p.SkuCode) && !string.IsNullOrWhiteSpace(p.Uom))
                .GroupBy(p => p.SkuCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Uom).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var sheet = workbook.Worksheets.Add("SkuUomData");
            sheet.Visibility = XLWorksheetVisibility.Hidden;

            for (int i = 0; i < skus.Count; i++)
            {
                var skuCode = skus[i].Code;
                if (!uomsBySku.TryGetValue(skuCode, out var skuUoms) || skuUoms.Count == 0)
                {
                    continue;
                }

                int row = i + 1; // 1-based, matches this SKU's row in SkuList
                for (int col = 0; col < skuUoms.Count; col++)
                {
                    sheet.Cell(row, col + 1).Value = skuUoms[col];
                }

                var rangeName = $"SKU_{row}";
                if (!workbook.NamedRanges.Any(n => n.Name.Equals(rangeName, StringComparison.OrdinalIgnoreCase)))
                {
                    var namedRange = sheet.Range(row, 1, row, skuUoms.Count);
                    workbook.NamedRanges.Add(rangeName, namedRange);
                }
            }
        }

        public byte[] GenerateErrorReportExcel(ImportSalesInvoiceResult result)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var sheet = workbook.Worksheets.Add("Errors");

            var headers = result.OriginalHeaders.Count > 0 ? result.OriginalHeaders : new List<string>
            {
                "InvoiceCode", "InvoiceDate", "CustomerCode", "OrderType",
                "SalesManName", "SkuCode", "UOM", "Quantity"
            };

            for (int i = 0; i < headers.Count; i++)
                sheet.Cell(1, i + 1).Value = headers[i];

            var errorColumn = headers.Count + 1;
            sheet.Cell(1, errorColumn).Value = "Error";
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDECEA");

            var issuesByRow = result.Issues
                .GroupBy(issue => issue.RowNumber)
                .OrderBy(group => group.Key);

            int excelRow = 2;
            foreach (var group in issuesByRow)
            {
                result.RawValuesByRow.TryGetValue(group.Key, out var rawValues);

                for (int i = 0; i < headers.Count; i++)
                {
                    string? value = null;
                    rawValues?.TryGetValue(headers[i], out value);
                    sheet.Cell(excelRow, i + 1).Value = value ?? string.Empty;
                }

                var errorCell = sheet.Cell(excelRow, errorColumn);
                errorCell.Value = string.Join("; ", group.Select(issue => issue.Message).Distinct());
                errorCell.Style.Font.FontColor = XLColor.FromHtml("#A32D2D");
                errorCell.Style.Font.Bold = true;

                excelRow++;
            }

            sheet.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }
    }
}