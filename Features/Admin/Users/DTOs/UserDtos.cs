namespace STTproject.Features.Admin.Users.DTOs
{
    public class UserListDto
    {
        public int UserId { get; set; }
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
        // public int? CreatedBy { get; set; }
        // public int? UpdatedBy { get; set; }
    }

    public class UserCreateDto
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; }
        // public int? CreatedBy { get; set; }
    }

    public class UserUpdateDto
    {
        public int UserId { get; set; }       // 👈 add this
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; }
        // public int? UpdatedBy { get; set; }
    }
}