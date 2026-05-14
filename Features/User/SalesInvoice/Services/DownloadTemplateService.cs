using ClosedXML.Excel;
using Microsoft.JSInterop;
using STTproject.Features.User.MapItem.DTOs;

namespace STTproject.Features.User.SalesInvoice.Services
{
    public class DownloadTemplateService
    {
        private readonly IJSRuntime _jsRuntime;

        public DownloadTemplateService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task GenerateAndDownloadExcelAsync(List<TemplateRow> templateData)
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template");

                // Add headers for sales invoice template
                worksheet.Cell(1, 1).Value = "InvoiceDate"; //Date validation (dd/MM/yyyy)
                worksheet.Cell(1, 2).Value = "CustomerCode"; 
                worksheet.Cell(1, 3).Value = "CustomerBranch"; //allow null or empty for customer branch then autofill with "Main Branch" in the system
                worksheet.Cell(1, 4).Value = "OrderType"; //OrderType dropdown (invoice, credit)
                worksheet.Cell(1, 5).Value = "SkuCode";
                worksheet.Cell(1, 6).Value = "UOM"; //drpdown with values from UOM column in Item table
                worksheet.Cell(1, 7).Value = "Quantity"; //Quantity must be whole number > 0
                worksheet.SheetView.FreezeRows(1);

                // Add data validation for InvoiceDate column
                var dateValidation = worksheet.Range("A2:A1048576").CreateDataValidation();
                dateValidation.Date.Between(
                    new DateTime(2000, 1, 1),
                    new DateTime(2100, 12, 31)
                );
                worksheet.Column(1).Style.DateFormat.Format = "dd/MM/yyyy";

                // Add dropdown for OrderType
                var orderTypeRange = worksheet.Range("D2:D1048576").CreateDataValidation();
                orderTypeRange.List("Invoice,Credit");

                // Add data validation for Quantity column
                worksheet.Cell(2, 7).CreateDataValidation().WholeNumber.GreaterThan(0);
                var qtyValidation = worksheet.Range("G2:G1048576").CreateDataValidation();
                qtyValidation.WholeNumber.EqualOrGreaterThan(1);

                // Style headers
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                worksheet.Columns().AdjustToContents();
                worksheet.Protect("!@#adhid");

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
