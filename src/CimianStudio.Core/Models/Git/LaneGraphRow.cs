namespace CimianStudio.Core.Models.Git;

public sealed record LaneLine(int FromColumn, int ToColumn, bool IsTopHalf, int ColorIndex);

public sealed record LaneGraphRow(
    int CommitColumn,
    int TotalColumns,
    IReadOnlyList<LaneLine> Lines,
    int ColorIndex);
