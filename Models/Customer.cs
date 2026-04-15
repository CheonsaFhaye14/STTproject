namespace STTproject.Models
{
    public class Customer
    {
        public int CustomerId { get; set; }
        public String CustomerType { get; set; } = string.Empty;
        public String CustomerCode { get; set; } = string.Empty;
        public String CustomerName { get; set; } = string.Empty;
        public String CustomerAddress { get; set; } = string.Empty;
    }
}
