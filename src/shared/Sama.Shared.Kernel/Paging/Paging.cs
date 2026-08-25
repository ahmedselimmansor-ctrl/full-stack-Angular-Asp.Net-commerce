namespace Sama.Shared.Kernel.Paging;

/// <summary>
/// A page request. <see cref="PageSize"/> is clamped rather than validated away, because
/// an unbounded page size is a cheap way for a caller to turn one request into a table scan.
/// </summary>
public sealed record PageRequest
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public PageRequest(int page = 1, int pageSize = DefaultPageSize)
    {
        Page = page < 1 ? 1 : page;
        PageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>One page of results plus enough metadata for a client to render pagination.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(PageRequest request)
        => new([], request.Page, request.PageSize, 0);
}
