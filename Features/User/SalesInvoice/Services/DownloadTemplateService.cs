using ClosedXML.Excel;
using Microsoft.JSInterop;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.SalesInvoice.Services
{
    public class DownloadTemplateService
    {
        private readonly ISalesInvoiceService _salesInvoiceService;

        public async Task GenerateAndDownloadExcelAsync(List<TemplateRow> templateData)
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template");
                worksheet.Protect("!@#adhid");


                // Add headers
                worksheet.Cell(1, 1).Value = "InvoiceDate";
                worksheet.Cell(1, 2).Value = "CustomerCode";
                worksheet.Cell(1, 3).Value = "CustomerBranch";
                worksheet.Cell(1, 4).Value = "OrderType";
                worksheet.Cell(1, 5).Value = "SkuCode";
                worksheet.Cell(1, 6).Value = "UOM";
                worksheet.Cell(1, 7).Value = "Quantity";
                worksheet.SheetView.FreezeRows(1);
                worksheet.Columns().AdjustToContents();
                // Style headers
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                // Add data rows with empty duplication rows for each item
                int currentRow = 2;

                for (int i = 0; i < templateData.Count; i++)
                {
                    var item = templateData[i];

                    // Add main data row
                      (worksheet, currentRow, item, locked: true);
                    currentRow++;

                    // Add empty rows for duplicating multiple UOMs for this item
                    for (int j = 0; j < emptyRowsPerItem; j++)
                    {
                        // Pre-fill locked columns with same item data, leave UOM fields empty
                        worksheet.Cell(currentRow, 1).Value = item.SubDistributorCode;
                        worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                        worksheet.Cell(currentRow, 2).Value = item.Principal;
                        worksheet.Cell(currentRow, 2).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                        worksheet.Cell(currentRow, 3).Value = item.CompanyItemCode;
                        worksheet.Cell(currentRow, 3).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                        worksheet.Cell(currentRow, 4).Value = item.CompanyItemName;
                        worksheet.Cell(currentRow, 4).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                        // Empty unlocked columns for user to fill in
                        for (int col = 5; col <= 9; col++)
                        {
                            worksheet.Cell(currentRow, col);
                            worksheet.Cell(currentRow, col).Style.Protection.Locked = false;
                        }

                        currentRow++;
                    }
                }

                // Add data validations
                int lastDataRow = currentRow - 1;

                // Conversion validation: Decimal > 0
                var conversionRange = worksheet.Range($"H2:H{lastDataRow}");
                var conversionValidation = conversionRange.CreateDataValidation();
                conversionValidation.Decimal.GreaterThan(0);
                conversionValidation.IgnoreBlanks = true;
                conversionValidation.ShowInputMessage = true;
                conversionValidation.InputTitle = "Conversion";
                conversionValidation.InputMessage = "Must be a number greater than 0. Example: 1.5";
                conversionValidation.ShowErrorMessage = true;
                conversionValidation.ErrorStyle = ClosedXML.Excel.XLErrorStyle.Stop;
                conversionValidation.ErrorTitle = "Invalid Conversion Value";
                conversionValidation.ErrorMessage = "Conversion must be a number greater than 0.";

                // Price validation: Decimal > 0
                var priceRange = worksheet.Range($"I2:I{lastDataRow}");
                var priceValidation = priceRange.CreateDataValidation();
                priceValidation.Decimal.GreaterThan(0);
                priceValidation.IgnoreBlanks = true;
                priceValidation.ShowInputMessage = true;
                priceValidation.InputTitle = "Price";
                priceValidation.InputMessage = "Must be a number greater than 0. Example: 10.50";
                priceValidation.ShowErrorMessage = true;
                priceValidation.ErrorStyle = ClosedXML.Excel.XLErrorStyle.Stop;
                priceValidation.ErrorTitle = "Invalid Price Value";
                priceValidation.ErrorMessage = "Price must be a number greater than 0.";

                // UOM validation: dropdown from existing mapped UOMs, but allow new entries too
                if (uomSourceRange != null)
                {
                    var uomRange = worksheet.Range($"G2:G{lastDataRow}");
                    var uomValidation = uomRange.CreateDataValidation();
                    uomValidation.List(uomSourceRange);
                    uomValidation.IgnoreBlanks = true;
                    uomValidation.ShowInputMessage = true;
                    uomValidation.InputTitle = "Unit of Measure (UOM)";
                    uomValidation.InputMessage = "Select from dropdown or type a new UOM code.";
                    uomValidation.ShowErrorMessage = false; // Allow custom entries
                }

                // Auto-fit column widths
                worksheet.Columns().AdjustToContents();

                // Download
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    stream.Position = 0;

                    var fileName = $"MapItemTemplate_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    await _jsRuntime.InvokeVoidAsync("downloadFile", stream.ToArray(), fileName,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                }
            }
        }

    }
}
