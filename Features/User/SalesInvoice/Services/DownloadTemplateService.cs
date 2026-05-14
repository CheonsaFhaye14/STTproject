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
                worksheet.Protect("!@#adhid");

                // Add headers for sales invoice template
                worksheet.Cell(1, 1).Value = "InvoiceDate";
                worksheet.Cell(1, 2).Value = "CustomerCode";
                worksheet.Cell(1, 3).Value = "CustomerBranch";
                worksheet.Cell(1, 4).Value = "OrderType";
                worksheet.Cell(1, 5).Value = "SkuCode";
                worksheet.Cell(1, 6).Value = "UOM";
                worksheet.Cell(1, 7).Value = "Quantity";
                worksheet.SheetView.FreezeRows(1);

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
