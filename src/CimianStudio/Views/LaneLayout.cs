namespace CimianStudio.Views;

using CimianStudio.Core.Models.Git;
using CimianStudio.Core.Services;

/// <summary>
/// Pre-computes per-row lane graph data from a topo-sorted commit list.
/// The algorithm tracks which SHA each lane column is "waiting for" and
/// assigns incoming/outgoing lines accordingly.
/// </summary>
internal static class LaneLayout
{
    private const int PaletteSize = 8;

    public static IReadOnlyList<LaneGraphRow> Compute(IReadOnlyList<GitCommit> commits)
    {
        var result = new List<LaneGraphRow>(commits.Count);
        var lanes = new List<string?>();   // lanes[i] = SHA this lane is tracking (null = empty)
        var colors = new List<int>();       // colors[i] = palette index for lane i
        var nextColor = 0;

        foreach (var commit in commits)
        {
            var sha = string.IsNullOrEmpty(commit.FullSha) ? commit.Sha : commit.FullSha;
            var parents = commit.ParentShas ?? [];

            // --- Find or claim a lane slot for this commit. ---
            bool isNewLane;
            var myLane = lanes.IndexOf(sha);
            if (myLane < 0)
            {
                isNewLane = true;
                var empty = lanes.IndexOf(null);
                if (empty < 0)
                {
                    myLane = lanes.Count;
                    lanes.Add(sha);
                    colors.Add(nextColor++ % PaletteSize);
                }
                else
                {
                    myLane = empty;
                    lanes[myLane] = sha;
                    colors[myLane] = nextColor++ % PaletteSize;
                }
            }
            else
            {
                isNewLane = false;
            }

            // --- Top-half lines: from current lane positions to the commit dot. ---
            var lines = new List<LaneLine>();
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == null) continue;
                if (i == myLane && isNewLane) continue; // brand-new lane, nothing incoming
                var dest = string.Equals(lanes[i], sha, StringComparison.Ordinal) ? myLane : i;
                lines.Add(new LaneLine(i, dest, IsTopHalf: true, colors[i]));
            }

            // --- Free duplicate lanes that converge at this commit. ---
            for (int i = 0; i < lanes.Count; i++)
            {
                if (i != myLane && string.Equals(lanes[i], sha, StringComparison.Ordinal))
                    lanes[i] = null;
            }

            // --- Assign parents to lanes. Track which slots are newly created. ---
            var newParentSlots = new HashSet<int>();
            if (parents.Count == 0)
            {
                lanes[myLane] = null; // root commit — close the lane
            }
            else
            {
                lanes[myLane] = parents[0]; // first parent continues in the same lane

                for (int p = 1; p < parents.Count; p++)
                {
                    var pSha = parents[p];
                    if (lanes.IndexOf(pSha) >= 0) continue; // already tracked

                    var slot = lanes.IndexOf(null);
                    if (slot < 0)
                    {
                        slot = lanes.Count;
                        lanes.Add(pSha);
                        colors.Add(nextColor++ % PaletteSize);
                    }
                    else
                    {
                        lanes[slot] = pSha;
                        colors[slot] = nextColor++ % PaletteSize;
                    }
                    newParentSlots.Add(slot);
                }
            }

            // --- Bottom-half lines: from the commit dot to next lane positions. ---
            // Existing lanes (including myLane with first parent) pass straight through.
            // Newly-created parent lanes only get the diagonal branch arm below.
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == null) continue;
                if (newParentSlots.Contains(i)) continue; // drawn by the branch-arm loop below
                lines.Add(new LaneLine(i, i, IsTopHalf: false, colors[i]));
            }
            // Diagonal branch arms from the commit dot to each extra parent's lane.
            for (int p = 1; p < parents.Count; p++)
            {
                var destLane = lanes.IndexOf(parents[p]);
                if (destLane >= 0)
                    lines.Add(new LaneLine(myLane, destLane, IsTopHalf: false, colors[myLane]));
            }

            // --- Compute the width (last active column + 1). ---
            var totalCols = myLane + 1;
            for (int i = 0; i < lanes.Count; i++)
                if (lanes[i] != null && i >= totalCols) totalCols = i + 1;

            result.Add(new LaneGraphRow(myLane, totalCols, lines, colors[myLane]));
        }

        return result;
    }
}
