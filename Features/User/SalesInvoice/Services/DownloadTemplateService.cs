using ClosedXML.Excel;
using Microsoft.JSInterop;
using STTproject.Features.User.MapItem.DTOs;
using STTproject.Services;

namespace STTproject.Features.User.SalesInvoice.Services
{
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

                // OrderType list (hidden sheet) to back the dropdown reliably
                var orderTypes = new[] { "Invoice", "Credit" };
                var orderTypeSheet = workbook.Worksheets.Add("OrderTypeList");
                orderTypeSheet.Visibility = XLWorksheetVisibility.Hidden;
                for (int i = 0; i < orderTypes.Length; i++)
                {
                    orderTypeSheet.Cell(i + 1, 1).Value = orderTypes[i];
                }
                var orderTypeSourceRange = orderTypeSheet.Range(1, 1, orderTypes.Length, 1);

                // Add headers for sales invoice template (no CustomerBranch)
                worksheet.Cell(1, 1).Value = "InvoiceCode";
                worksheet.Cell(1, 2).Value = "InvoiceDate";
                worksheet.Cell(1, 3).Value = "CustomerCode";
                worksheet.Cell(1, 4).Value = "OrderType";
                worksheet.Cell(1, 5).Value = "SalesManName";
                worksheet.Cell(1, 6).Value = "SkuCode";
                worksheet.Cell(1, 7).Value = "UOM";
                worksheet.Cell(1, 8).Value = "Quantity";
                worksheet.Row(1).Style.Protection.Locked = true;
                // Unlock whole input columns (A..H) and scratch columns (I..Z) so there is no fixed row limit.
                worksheet.Columns(1, 8).Style.Protection.Locked = false;
                worksheet.Columns(9, 26).Style.Protection.Locked = false;
                // Ensure header row remains locked
                worksheet.Range("A1:H1").Style.Protection.Locked = true;
                worksheet.SheetView.FreezeRows(1);

                // format codes as text to prevent Excel auto-formatting (e.g. long numeric codes, leading zeros)
                // Format key code columns as text to prevent Excel auto-formatting
                worksheet.Column(1).Style.NumberFormat.Format = "@"; // InvoiceCode
                worksheet.Column(3).Style.NumberFormat.Format = "@"; // CustomerCode
                worksheet.Column(4).Style.NumberFormat.Format = "@"; // OrderType (codes)
                worksheet.Column(6).Style.NumberFormat.Format = "@"; // SkuCode
                worksheet.Column(7).Style.NumberFormat.Format = "@"; // UOM


                // Add data validation for InvoiceDate column
                var dateRange = worksheet.Range("B2:B1048576");
                dateRange.Style.DateFormat.Format = "dd/MM/yyyy";
                var dateValidation = dateRange.CreateDataValidation();
                dateValidation.Date.Between(
                    new DateTime(2000, 1, 1),
                    new DateTime(2100, 12, 31)
                );
                dateValidation.IgnoreBlanks = true;
                dateValidation.ShowInputMessage = true;
                dateValidation.InputTitle = "Invoice Date";
                dateValidation.InputMessage = "Enter date in format: dd/MM/yyyy (e.g., 15/05/2026)";
                dateValidation.ShowErrorMessage = true;
                dateValidation.ErrorTitle = "Invalid Date";
                dateValidation.ErrorMessage = "Please enter a valid date.";

                // Add dropdown for OrderType (column D)
                var orderTypeRange = worksheet.Range("D2:D1048576").CreateDataValidation();
                orderTypeRange.List(orderTypeSourceRange);
                orderTypeRange.InCellDropdown = true;
                orderTypeRange.IgnoreBlanks = true;
                orderTypeRange.InputMessage = "Select from dropdown.";
                orderTypeRange.ShowInputMessage = true;
                orderTypeRange.InputTitle = "Order Type";
                orderTypeRange.ShowErrorMessage = true;
                orderTypeRange.ErrorStyle = ClosedXML.Excel.XLErrorStyle.Stop;
                orderTypeRange.ErrorTitle = "Invalid Order Type";
                orderTypeRange.ErrorMessage = "Please select either Invoice or Credit";

                // Add data validation for Quantity column
                var qtyValidation = worksheet.Range("H2:H1048576").CreateDataValidation();
                qtyValidation.WholeNumber.GreaterThan(0);
                qtyValidation.IgnoreBlanks = true;
                qtyValidation.ShowInputMessage = true;
                qtyValidation.InputTitle = "Quantity";
                qtyValidation.InputMessage = "Must be a whole number greater than 0.";
                qtyValidation.ShowErrorMessage = true;
                qtyValidation.ErrorTitle = "Invalid Quantity";
                qtyValidation.ErrorMessage = "Quantity must be a whole number greater than 0";

                // UOM validation: dropdown from existing mapped UOMs (column G)
                if (uomSourceRange != null)
                {
                    var uomRange = worksheet.Range("G2:G1048576");
                    var uomValidation = uomRange.CreateDataValidation();
                    uomValidation.List(uomSourceRange);
                    uomValidation.IgnoreBlanks = true;
                    uomValidation.ShowInputMessage = true;
                    uomValidation.InputTitle = "Unit of Measure (UOM)";
                    uomValidation.InputMessage = "Select from dropdown.";
                    uomValidation.ShowErrorMessage = true;
                    uomValidation.ErrorStyle = ClosedXML.Excel.XLErrorStyle.Stop;
                    uomValidation.ErrorTitle = "Invalid UOM";
                    uomValidation.ErrorMessage = "Please select a valid UOM from the dropdown.";
                }

                // Style headers
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                worksheet.Columns().AdjustToContents();

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
    }
}

