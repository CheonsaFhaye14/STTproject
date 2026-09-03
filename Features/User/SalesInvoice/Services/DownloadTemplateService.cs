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
            List<(string SkuCode, string Uom, decimal Price)>? prices = null,
            string? SubDistributorName = null)
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template");
                worksheet.Protect("!@#adhid");

                var customerTable = BuildHiddenPickerSheet(workbook, "CustomerList", customers);
                var skuTable = BuildHiddenPickerSheet(workbook, "SkuList", skus);
                var uomListRange = BuildHiddenListSheet(workbook, "UOMList", uoms);
                var priceTable = BuildHiddenPriceSheet(workbook, "PriceList", prices);

                if (uomListRange != null && !workbook.NamedRanges.Any(n => n.Name.Equals("AllUomList", StringComparison.OrdinalIgnoreCase)))
                {
                    workbook.NamedRanges.Add("AllUomList", uomListRange);
                }

                BuildSkuUomNamedRanges(workbook, skus, prices);

                var orderTypes = new[] { "Invoice", "Credit" };
                var orderTypeSheet = workbook.Worksheets.Add("OrderTypeList");
                orderTypeSheet.Visibility = XLWorksheetVisibility.Hidden;
                for (int i = 0; i < orderTypes.Length; i++)
                {
                    orderTypeSheet.Cell(i + 1, 1).Value = orderTypes[i];
                }
                var orderTypeSourceRange = orderTypeSheet.Range(1, 1, orderTypes.Length, 1);

                // ── Headers (9 columns) ──
                // A InvoiceCode | B InvoiceDate | C Customer picker | D OrderType
                // E Item picker | F UOM | G Quantity | H Amount (auto) | I SalesManName
                worksheet.Cell(1, 1).Value = "InvoiceCode";
                worksheet.Cell(1, 2).Value = "InvoiceDate";
                worksheet.Cell(1, 3).Value = "Customer (Code or Name)";
                worksheet.Cell(1, 4).Value = "OrderType";
                worksheet.Cell(1, 5).Value = "Item (SKU or Name)";
                worksheet.Cell(1, 6).Value = "UOM";
                worksheet.Cell(1, 7).Value = "Quantity";
                worksheet.Cell(1, 8).Value = "Amount";
                worksheet.Cell(1, 9).Value = "SalesManName";
                worksheet.Row(1).Style.Protection.Locked = true;

                worksheet.Columns(1, 9).Style.Protection.Locked = false;
                worksheet.Columns(10, 30).Style.Protection.Locked = false;
                worksheet.Range("A1:I1").Style.Protection.Locked = true;
                worksheet.SheetView.FreezeRows(1);

                worksheet.Column(1).Style.NumberFormat.Format = "@"; // InvoiceCode — avoid Excel stripping leading zeros

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

                // ── Customer picker dropdown (C) — "Code - Name" list ──
                if (customerTable != null)
                {
                    var customerValidation = worksheet.Range($"C2:C{maxRow}").CreateDataValidation();
                    customerValidation.List(customerTable.Value.PickerColumn);
                    customerValidation.InCellDropdown = true;
                    customerValidation.IgnoreBlanks = true;
                    customerValidation.ShowInputMessage = true;
                    customerValidation.InputTitle = "Customer";
                    customerValidation.InputMessage = "Search/select by code or name.";
                    customerValidation.ShowErrorMessage = true;
                    customerValidation.ErrorStyle = XLErrorStyle.Stop;
                    customerValidation.ErrorTitle = "Invalid Customer";
                    customerValidation.ErrorMessage = "Please select a valid customer from the dropdown.";
                }

                // ── OrderType dropdown (D) ──
                var orderTypeRange = worksheet.Range($"D2:D{maxRow}").CreateDataValidation();
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

                // ── Item picker dropdown (E) — "SkuCode - ItemName" list ──
                if (skuTable != null)
                {
                    var skuValidation = worksheet.Range($"E2:E{maxRow}").CreateDataValidation();
                    skuValidation.List(skuTable.Value.PickerColumn);
                    skuValidation.InCellDropdown = true;
                    skuValidation.IgnoreBlanks = true;
                    skuValidation.ShowInputMessage = true;
                    skuValidation.InputTitle = "Item";
                    skuValidation.InputMessage = "Search/select by SKU code or item name.";
                    skuValidation.ShowErrorMessage = true;
                    skuValidation.ErrorStyle = XLErrorStyle.Stop;
                    skuValidation.ErrorTitle = "Invalid Item";
                    skuValidation.ErrorMessage = "Please select a valid item from the dropdown.";
                }

                // ── UOM dropdown (F) — cascades off the item picker in column E ──
                // MATCH against SkuList!C:C (the picker-label column), NOT A:A — E holds the
                // combined "Code - Name" string, which only exists in column C of SkuList.
                // Row position still lines up with the SKU_<n> named ranges since C and A
                // share the same row numbering.
                if (uomListRange != null && skuTable != null)
                {
                    var uomValidation = worksheet.Range($"F2:F{maxRow}").CreateDataValidation();
                    var uomFormula = "=INDIRECT(\"SKU_\"&MATCH(E2,SkuList!C:C,0))";
                    uomValidation.List(uomFormula, true);
                    uomValidation.InCellDropdown = true;
                    uomValidation.IgnoreBlanks = true;
                    uomValidation.ShowInputMessage = true;
                    uomValidation.InputTitle = "Unit of Measure (UOM)";
                    uomValidation.InputMessage = "Select the item first — the UOM list narrows to it.";
                    uomValidation.ShowErrorMessage = true;
                    uomValidation.ErrorStyle = XLErrorStyle.Stop;
                    uomValidation.ErrorTitle = "Invalid UOM";
                    uomValidation.ErrorMessage = "Please select a valid UOM from the dropdown.";
                }
                else if (uomListRange != null)
                {
                    var uomValidation = worksheet.Range($"F2:F{maxRow}").CreateDataValidation();
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

                // ── Quantity validation (G) ──
                var qtyValidation = worksheet.Range($"G2:G{maxRow}").CreateDataValidation();
                qtyValidation.WholeNumber.GreaterThan(0);
                qtyValidation.IgnoreBlanks = true;
                qtyValidation.ShowInputMessage = true;
                qtyValidation.InputTitle = "Quantity";
                qtyValidation.InputMessage = "Must be a whole number greater than 0.";
                qtyValidation.ShowErrorMessage = true;
                qtyValidation.ErrorTitle = "Invalid Quantity";
                qtyValidation.ErrorMessage = "Quantity must be a whole number greater than 0";

                // ── Amount auto-calc (H) — locked formula ──
                // Quantity is G, UOM is F. Plain SkuCode isn't stored anywhere on this sheet
                // directly (E holds the combined "Code - Name" picker text), so it's derived
                // inline via VLOOKUP(E, SkuList!C:A, 2, FALSE) — reversed range C:A puts the
                // picker label leftmost so VLOOKUP can look it up and return column A (the code).
                if (priceTable != null)
                {
                    for (int row = 2; row <= formulaFillRows; row++)
                    {
                        worksheet.Cell(row, 8).FormulaA1 =
                            $"=IFERROR(G{row}*VLOOKUP(INDEX(SkuList!A:A,MATCH(E{row},SkuList!C:C,0))&\"|\"&F{row},PriceList!A:B,2,FALSE),0)";
                    }
                }
                worksheet.Column(8).Style.Protection.Locked = true;

                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                worksheet.Column(1).Width = 16;  // InvoiceCode
                worksheet.Column(2).Width = 14;  // InvoiceDate
                worksheet.Column(3).Width = CalcColumnWidth(customers?.Select(c => $"{c.Code} - {c.Name}"), "Customer (Code or Name)", 20, 55);
                worksheet.Column(4).Width = 12;  // OrderType
                worksheet.Column(5).Width = CalcColumnWidth(skus?.Select(s => $"{s.Code} - {s.Name}"), "Item (SKU or Name)", 20, 55);
                worksheet.Column(6).Width = CalcColumnWidth(uoms, "UOM", 10, 20);
                worksheet.Column(7).Width = 12; // Quantity
                worksheet.Column(8).Width = 14; // Amount
                worksheet.Column(9).Width = 20; // SalesManName

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    stream.Position = 0;
                    var fileName = $"{SubDistributorName}_SalesInvoiceTemplate_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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

        private static (IXLRange PickerColumn, IXLRange FullTable)? BuildHiddenPickerSheet(
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
                sheet.Cell(i + 1, 3).Value = string.IsNullOrWhiteSpace(rows[i].Name)
                    ? rows[i].Code
                    : $"{rows[i].Code} - {rows[i].Name}";
            }

            var pickerColumn = sheet.Range(1, 3, rows.Count, 3);
            var fullTable = sheet.Range(1, 1, rows.Count, 3);
            return (pickerColumn, fullTable);
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

                int row = i + 1;
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