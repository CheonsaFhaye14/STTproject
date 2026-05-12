using ClosedXML.Excel;
using Microsoft.JSInterop;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.MapItem.Services;

public class DownloadTemplateService
{
    private readonly IMapItemService _mapItemService;
    private readonly IJSRuntime _jsRuntime;

    public DownloadTemplateService(IMapItemService mapItemService, IJSRuntime jsRuntime)
    {
        _mapItemService = mapItemService;
        _jsRuntime = jsRuntime;
    }

    public async Task GenerateAndDownloadExcelAsync(List<TemplateRow> templateData)
    {
        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Template");
            worksheet.Protect("!@#adhid");
            var existingUoms = await _mapItemService.GetCompanyItemUomsAsync(0);
            IXLRange? uomSourceRange = null;

            if (existingUoms.Count > 0)
            {
                var uomSheet = workbook.Worksheets.Add("UOMList");
                uomSheet.Visibility = XLWorksheetVisibility.Hidden;

                for (int i = 0; i < existingUoms.Count; i++)
                {
                    uomSheet.Cell(i + 1, 1).Value = existingUoms[i];
                }

                uomSourceRange = uomSheet.Range(1, 1, existingUoms.Count, 1);
            }

            // Add headers
            worksheet.Cell(1, 1).Value = "SubDistributorCode";
            worksheet.Cell(1, 2).Value = "Principal";
            worksheet.Cell(1, 3).Value = "CompanyItemCode";
            worksheet.Cell(1, 4).Value = "CompanyItemName";
            worksheet.Cell(1, 5).Value = "SubdItemCode";
            worksheet.Cell(1, 6).Value = "SubdItemName";
            worksheet.Cell(1, 7).Value = "UOM";
            worksheet.Cell(1, 8).Value = "Conversion";
            worksheet.Cell(1, 9).Value = "Price";
            worksheet.SheetView.FreezeRows(1);
            worksheet.Columns().AdjustToContents();
            // Style headers
            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

            // Add data rows with empty duplication rows for each item
            int currentRow = 2;
            const int emptyRowsPerItem = 3; // Allow 3 empty rows per item for UOM variations

            for (int i = 0; i < templateData.Count; i++)
            {
                var item = templateData[i];

                // Add main data row
                AddTemplateDataRow(worksheet, currentRow, item, locked: true);
                currentRow++;

                // Add empty rows for duplicating multiple UOMs for this item
                for (int j = 0; j < emptyRowsPerItem; j++)
                {
                    // Pre-fill locked columns with same item data, leave UOM fields empty
                    worksheet.Cell(currentRow, 1).Value = item.SubDistributorCode;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                    worksheet.Cell(currentRow, 1).Style.Protection.Locked = true;

                    worksheet.Cell(currentRow, 2).Value = item.Principal;
                    worksheet.Cell(currentRow, 2).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                    worksheet.Cell(currentRow, 2).Style.Protection.Locked = true;

                    worksheet.Cell(currentRow, 3).Value = item.CompanyItemCode;
                    worksheet.Cell(currentRow, 3).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                    worksheet.Cell(currentRow, 3).Style.Protection.Locked = true;

                    worksheet.Cell(currentRow, 4).Value = item.CompanyItemName;
                    worksheet.Cell(currentRow, 4).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                    worksheet.Cell(currentRow, 4).Style.Protection.Locked = true;

                    // Empty unlocked columns for user to fill in
                    for (int col = 5; col <= 9; col++)
                    {
                        worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.White;
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

    private void AddTemplateDataRow(IXLWorksheet worksheet, int row, TemplateRow item, bool locked)
    {
        // Locked columns (read-only): SubDistributorCode, Principal, CompanyItemCode, CompanyItemName
        worksheet.Cell(row, 1).Value = item.SubDistributorCode;
        worksheet.Cell(row, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        worksheet.Cell(row, 1).Style.Protection.Locked = true;

        worksheet.Cell(row, 2).Value = item.Principal;
        worksheet.Cell(row, 2).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        worksheet.Cell(row, 2).Style.Protection.Locked = true;

        worksheet.Cell(row, 3).Value = item.CompanyItemCode;
        worksheet.Cell(row, 3).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        worksheet.Cell(row, 3).Style.Protection.Locked = true;

        worksheet.Cell(row, 4).Value = item.CompanyItemName;
        worksheet.Cell(row, 4).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        worksheet.Cell(row, 4).Style.Protection.Locked = true;

        // Unlocked columns: SubdItemCode, SubdItemName, UOM, Conversion, Price
        worksheet.Cell(row, 5).Value = item.SubdItemCode;
        worksheet.Cell(row, 5).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.White;
        worksheet.Cell(row, 5).Style.Protection.Locked = false;

        worksheet.Cell(row, 6).Value = item.SubdItemName;
        worksheet.Cell(row, 6).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.White;
        worksheet.Cell(row, 6).Style.Protection.Locked = false;

        worksheet.Cell(row, 7).Value = item.UOM;
        worksheet.Cell(row, 7).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.White;
        worksheet.Cell(row, 7).Style.Protection.Locked = false;

        worksheet.Cell(row, 8).Value = item.Conversion;
        worksheet.Cell(row, 8).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.White;
        worksheet.Cell(row, 8).Style.Protection.Locked = false;

        worksheet.Cell(row, 9).Value = item.Price;
        worksheet.Cell(row, 9).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.White;
        worksheet.Cell(row, 9).Style.Protection.Locked = false;
    }
}