using STTproject.Features.Admin.ImportTemplate.DTOs;

namespace STTproject.Features.Admin.ImportTemplate.Services;

/// <summary>
/// The assignment objective, compared lexicographically:
/// 1. more required template sheets matched wins;
/// 2. then the higher total score wins; scores within <see cref="Tolerance"/> are equal.
/// </summary>
public readonly record struct AssignmentObjective(int RequiredMatched, double TotalScore)
{
    public const double Tolerance = 1e-9;

    public int CompareTo(AssignmentObjective other)
    {
        if (RequiredMatched != other.RequiredMatched) return RequiredMatched.CompareTo(other.RequiredMatched);
        var d = TotalScore - other.TotalScore;
        return Math.Abs(d) <= Tolerance ? 0 : d > 0 ? 1 : -1;
    }
}

/// <summary>Template sheets and worksheets that are tangled together by equally good alternatives. Indexes into the matrix.</summary>
public sealed record AmbiguityGroup(IReadOnlyList<int> TemplateSheets, IReadOnlyList<int> Worksheets);

public sealed class AssignmentResult
{
    /// <summary>
    /// Per template sheet: the worksheet index in one optimal assignment, or -1 for none.
    /// For a template sheet in <see cref="AmbiguousTemplates"/> this is only one of several equally good answers,
    /// so callers must not read it as a decision.
    /// </summary>
    public int[] Assigned { get; init; } = Array.Empty<int>();

    public AssignmentObjective Objective { get; init; }

    /// <summary>Template sheets whose worksheet differs between equally good assignments (including "none" vs one).</summary>
    public HashSet<int> AmbiguousTemplates { get; init; } = new();

    /// <summary>Per template sheet: every worksheet index it takes in some optimal assignment; -1 means "unmatched".</summary>
    public IReadOnlyList<IReadOnlyCollection<int>> Options { get; init; } = Array.Empty<IReadOnlyCollection<int>>();

    public IReadOnlyList<AmbiguityGroup> Groups { get; init; } = Array.Empty<AmbiguityGroup>();

    /// <summary>More optimal assignments than <see cref="SheetAssigner.MaxOptimalSolutions"/>; the options may be incomplete.</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Chooses which worksheet each template sheet reads, as one global decision. Pure: works on a score matrix only.
/// - Each template sheet gets at most one worksheet; each worksheet is used at most once.
/// - Any template sheet, required or not, may stay unmatched. A score is never forced.
/// - Required sheets are maximized before score, so a high-scoring optional match can't displace a required one.
/// - Every optimal assignment is enumerated, so a tie is reported with all the sheets involved, never broken silently.
/// Exhaustive search with bounds: fine for the handful of template sheets a template has.
/// </summary>
public static class SheetAssigner
{
    public const int MaxOptimalSolutions = 1000;

    /// <param name="scores">[templateSheet, worksheet]; null = not eligible.</param>
    /// <param name="required">Per template sheet.</param>
    public static AssignmentResult Solve(double?[,] scores, bool[] required)
    {
        var t = scores.GetLength(0);
        var w = scores.GetLength(1);
        if (required.Length != t) throw new ArgumentException("required must have one entry per template sheet.", nameof(required));

        // Eligible worksheets per template sheet, best first, so good solutions are found early and pruning bites.
        var cands = new int[t][];
        for (var i = 0; i < t; i++)
        {
            var row = i;
            cands[i] = Enumerable.Range(0, w)
                .Where(j => scores[row, j].HasValue)
                .OrderByDescending(j => scores[row, j]!.Value)
                .ThenBy(j => j)
                .ToArray();
        }

        // ub[i]: the most template sheets i..end could still add, ignoring conflicts. An optimistic bound for pruning.
        var ub = new AssignmentObjective[t + 1];
        for (var i = t - 1; i >= 0; i--)
        {
            var has = cands[i].Length > 0;
            ub[i] = new AssignmentObjective(
                ub[i + 1].RequiredMatched + (required[i] && has ? 1 : 0),
                ub[i + 1].TotalScore + (has ? scores[i, cands[i][0]]!.Value : 0));
        }

        var used = new bool[w];
        var current = new int[t];
        Array.Fill(current, -1);

        // Pass 1: the best objective, and one assignment that reaches it.
        var best = new AssignmentObjective(-1, 0);
        var bestAssign = new int[t];
        Array.Fill(bestAssign, -1);

        void Find(int i, AssignmentObjective sofar)
        {
            var bound = new AssignmentObjective(sofar.RequiredMatched + ub[i].RequiredMatched, sofar.TotalScore + ub[i].TotalScore);
            if (bound.CompareTo(best) <= 0) return;           // can't beat what we have
            if (i == t) { best = sofar; Array.Copy(current, bestAssign, t); return; }

            foreach (var j in cands[i])
            {
                if (used[j]) continue;
                used[j] = true;
                current[i] = j;
                Find(i + 1, new AssignmentObjective(
                    sofar.RequiredMatched + (required[i] ? 1 : 0), sofar.TotalScore + scores[i, j]!.Value));
                used[j] = false;
            }
            current[i] = -1;
            Find(i + 1, sofar);                                // leave this template sheet unmatched
        }
        Find(0, new AssignmentObjective(0, 0));

        // Pass 2: every assignment with the same objective. Where they disagree, the choice is ambiguous.
        var options = Enumerable.Range(0, t).Select(_ => new SortedSet<int>()).ToArray();
        var found = 0;
        var truncated = false;
        Array.Fill(current, -1);

        void Collect(int i, AssignmentObjective sofar)
        {
            if (truncated) return;
            var bound = new AssignmentObjective(sofar.RequiredMatched + ub[i].RequiredMatched, sofar.TotalScore + ub[i].TotalScore);
            if (bound.CompareTo(best) < 0) return;             // can't even tie
            if (i == t)
            {
                if (sofar.CompareTo(best) != 0) return;
                for (var k = 0; k < t; k++) options[k].Add(current[k]);
                if (++found >= MaxOptimalSolutions) truncated = true;
                return;
            }

            foreach (var j in cands[i])
            {
                if (used[j]) continue;
                used[j] = true;
                current[i] = j;
                Collect(i + 1, new AssignmentObjective(
                    sofar.RequiredMatched + (required[i] ? 1 : 0), sofar.TotalScore + scores[i, j]!.Value));
                used[j] = false;
            }
            current[i] = -1;
            Collect(i + 1, sofar);
        }
        Collect(0, new AssignmentObjective(0, 0));

        var ambiguous = Enumerable.Range(0, t).Where(i => options[i].Count > 1).ToHashSet();

        return new AssignmentResult
        {
            Assigned = bestAssign,
            Objective = best,
            AmbiguousTemplates = ambiguous,
            Options = options.Select(o => (IReadOnlyCollection<int>)o.ToList()).ToList(),
            Groups = BuildGroups(t, w, ambiguous, options),
            Truncated = truncated
        };
    }

    // Template sheets that share candidate worksheets form one group, so the tester can say
    // "these sheets and these worksheets are mixed up" instead of listing pairs one by one.
    private static List<AmbiguityGroup> BuildGroups(int t, int w, HashSet<int> ambiguous, SortedSet<int>[] options)
    {
        var parent = Enumerable.Range(0, t + w).ToArray();
        int Root(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        foreach (var i in ambiguous)
            foreach (var j in options[i].Where(j => j >= 0))
                parent[Root(i)] = Root(t + j);

        return ambiguous
            .GroupBy(Root)
            .Select(g => new AmbiguityGroup(
                g.OrderBy(i => i).ToList(),
                g.SelectMany(i => options[i]).Where(j => j >= 0).Distinct().OrderBy(j => j).ToList()))
            .OrderBy(g => g.TemplateSheets[0])
            .ToList();
    }
}

/// <summary>One active template sheet and what the assignment decided for it.</summary>
public sealed class SheetAssignment
{
    public ImportTemplateSheetEditDto TemplateSheet { get; init; } = default!;

    /// <summary>The worksheet it reads. Null when unmatched, and null when ambiguous (see <see cref="Alternatives"/>).</summary>
    public WorksheetInspection? Worksheet { get; init; }
    public SheetMatchScore? Score { get; init; }

    /// <summary>Equally good assignments exist, so no worksheet was chosen.</summary>
    public bool IsAmbiguous { get; init; }

    /// <summary>The worksheets it could take in an equally good assignment. Empty unless ambiguous.</summary>
    public IReadOnlyList<WorksheetInspection> Alternatives { get; init; } = Array.Empty<WorksheetInspection>();

    /// <summary>It can also end up with no worksheet in an equally good assignment.</summary>
    public bool CanBeUnmatched { get; init; }
}

public sealed class WorkbookAssignment
{
    /// <summary>One entry per template sheet that isn't Ignore, in template order, matched or not.</summary>
    public List<SheetAssignment> Sheets { get; } = new();

    /// <summary>Visible worksheets that no template sheet took and that no ambiguity involves.</summary>
    public List<WorksheetInspection> Unclaimed { get; } = new();

    public IReadOnlyList<AmbiguityGroup> Groups { get; init; } = Array.Empty<AmbiguityGroup>();
    public bool Truncated { get; init; }
}

public static class WorkbookAssigner
{
    /// <summary>
    /// Scores every visible worksheet against every active template sheet, then assigns one-to-one.
    /// Hidden worksheets and Ignore template sheets never enter the matrix. Step 4.4 adds the missing/hidden reporting.
    /// </summary>
    public static WorkbookAssignment Assign(ImportTemplateEditDto template, IReadOnlyList<WorksheetInspection> inspections)
    {
        var visible = inspections.Where(s => s.IsVisible).ToList();
        var active = template.Sheets.Where(s => s.SheetMatchMode != SheetMatchModes.Ignore).ToList();

        var matrix = new double?[active.Count, visible.Count];
        var scores = new SheetMatchScore[active.Count, visible.Count];
        for (var i = 0; i < active.Count; i++)
            for (var j = 0; j < visible.Count; j++)
            {
                var s = TemplateSheetScorer.Score(active[i], visible[j]);
                scores[i, j] = s;
                if (s.Eligible) matrix[i, j] = s.Score;
            }

        var solved = SheetAssigner.Solve(matrix, active.Select(a => a.IsRequired).ToArray());
        var outcome = new WorkbookAssignment { Groups = solved.Groups, Truncated = solved.Truncated };
        var claimed = new HashSet<int>();

        for (var i = 0; i < active.Count; i++)
        {
            if (solved.AmbiguousTemplates.Contains(i))
            {
                var alts = solved.Options[i].Where(j => j >= 0).Select(j => visible[j]).ToList();
                foreach (var j in solved.Options[i].Where(j => j >= 0)) claimed.Add(j);
                outcome.Sheets.Add(new SheetAssignment
                {
                    TemplateSheet = active[i],
                    IsAmbiguous = true,
                    Alternatives = alts,
                    CanBeUnmatched = solved.Options[i].Contains(-1)
                });
                continue;
            }

            var j0 = solved.Assigned[i];
            if (j0 >= 0) claimed.Add(j0);
            outcome.Sheets.Add(new SheetAssignment
            {
                TemplateSheet = active[i],
                Worksheet = j0 >= 0 ? visible[j0] : null,
                Score = j0 >= 0 ? scores[i, j0] : null
            });
        }

        for (var j = 0; j < visible.Count; j++)
            if (!claimed.Contains(j)) outcome.Unclaimed.Add(visible[j]);

        return outcome;
    }
}