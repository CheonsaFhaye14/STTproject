namespace STTproject.Shared.Layout
{
    public partial class UserNavBar
    {
        private string MapItemHref => userContext.UserId.HasValue
            ? $"mapitem?uid={userContext.UserId.Value}"
            : "mapitem";
    }
}
