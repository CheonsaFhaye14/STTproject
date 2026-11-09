using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace STTproject.Features.Admin.Dashboard.Components
{
    /// <summary>
    /// Base for dashboard panels that hold one paged, sortable table.
    /// - Page size follows the table's available height (wwwroot/js/admin-dashboard.js),
    ///   so the page never scrolls; extra rows go to the next page.
    /// - Sorting: click a header to sort, click again to flip direction.
    /// </summary>
    public abstract class DashPanelBase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected IJSRuntime JS { get; set; } = default!;

        private IJSObjectReference? module;
        private DotNetObjectReference<DashPanelBase>? selfRef;
        private int watchId;

        // ---------- Paging ----------
        protected int PageSize { get; private set; } = 10;
        protected int CurrentPage { get; set; } = 1;

        protected int TotalPages(int count) => Math.Max(1, (int)Math.Ceiling(count / (double)PageSize));

        /// <summary>Rows for the current page (clamps the page if the list got shorter).</summary>
        protected List<T> PageOf<T>(IReadOnlyList<T> rows)
        {
            CurrentPage = Math.Clamp(CurrentPage, 1, TotalPages(rows.Count));
            return rows.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
        }

        protected void GoToPage(int page) => CurrentPage = page;

        /// <summary>Starts watching a table body's height. Call from OnAfterRenderAsync(firstRender).</summary>
        protected async Task WatchRowsAsync(ElementReference body, int rowHeight, int headerHeight = 32)
        {
            module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/admin-dashboard.js");
            selfRef ??= DotNetObjectReference.Create(this);
            watchId = await module.InvokeAsync<int>("watchRows", body, rowHeight, headerHeight, selfRef);
        }

        [JSInvokable]
        public Task SetPageSize(int rows)
        {
            rows = Math.Max(1, rows);
            if (rows == PageSize) return Task.CompletedTask;
            PageSize = rows;
            return InvokeAsync(StateHasChanged);
        }

        // ---------- Sorting ----------
        protected string SortKey { get; set; } = string.Empty;
        protected bool SortAsc { get; set; }

        /// <summary>Same column flips direction; a new column starts ascending unless startDescending.</summary>
        protected void ToggleSort(string key, bool startDescending = false)
        {
            if (SortKey == key) SortAsc = !SortAsc;
            else { SortKey = key; SortAsc = !startDescending; }
            CurrentPage = 1;
        }

        protected string SortClass(string key) => SortKey == key ? "sort on" : "sort";
        protected string SortArrow(string key) => SortKey != key ? "↕" : SortAsc ? "▲" : "▼";

        /// <summary>Sorts by the current SortKey using the given key selectors, then by the tie-breaker.</summary>
        protected List<T> Sorted<T>(IEnumerable<T> rows, IReadOnlyDictionary<string, Func<T, IComparable>> keys, Func<T, string> tieBreaker)
        {
            if (!keys.TryGetValue(SortKey, out var key))
                return rows.OrderBy(tieBreaker, StringComparer.OrdinalIgnoreCase).ToList();

            var ordered = SortAsc ? rows.OrderBy(key) : rows.OrderByDescending(key);
            return ordered.ThenBy(tieBreaker, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (module is not null)
                {
                    await module.InvokeVoidAsync("unwatch", watchId);
                    await module.DisposeAsync();
                }
            }
            catch (JSDisconnectedException) { /* circuit already gone */ }
            selfRef?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
