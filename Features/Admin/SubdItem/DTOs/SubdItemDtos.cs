namespace STTProject.Features.Admin.SubdItem.DTOs
{
    public class SubdItemListDto
    {
        public int SubDistributorId { get; set; }
        public int SubdItemId { get; set; }
        public string? SubdItemCode { get; set; }
        public string? SubdItemName { get; set; }
        public string? UomName { get; set; }
        public string? Price { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
    }
}