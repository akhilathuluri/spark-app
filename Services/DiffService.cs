using System.Text.RegularExpressions;

namespace Spark.Services;

public enum DiffType { Equal, Delete, Insert }

/// <summary>A contiguous span of tokens with the same diff operation.</summary>
public record DiffChunk(DiffType Type, string Text);

/// <summary>
/// Word-level diff between two strings using Longest Common Subsequence (LCS).
/// Tokens are split as: words, punctuation runs, and whitespace sequences.
/// Produces the minimal set of Equal/Delete/Insert chunks.
/// </summary>
public static class DiffService
{
    // Tokenize into: word-runs, non-word/non-space runs (punctuation), whitespace runs.
    // Keeping whitespace as tokens means spacing is preserved faithfully in the render.
    private static readonly Regex TokenRe = new(@"\w+|[^\w\s]+|\s+", RegexOptions.Compiled);

    // Bail-out guard: LCS is O(n*m) — cap to avoid freezing on very long text.
    private const int MaxTokens = 500;

    private static string[] Tokenize(string text) =>
        TokenRe.Matches(text).Select(m => m.Value).ToArray();

    /// <summary>
    /// Computes a word-level diff between <paramref name="original"/> and
    /// <paramref name="result"/>. Returns chunks ordered as they appear in the output.
    /// </summary>
    public static List<DiffChunk> Compute(string original, string result)
    {
        // Fast path: identical texts
        if (original == result)
            return [new DiffChunk(DiffType.Equal, original)];

        var a = Tokenize(original);
        var b = Tokenize(result);

        // Fallback for very long texts: treat as a single delete + insert
        if (a.Length > MaxTokens || b.Length > MaxTokens)
            return [new DiffChunk(DiffType.Delete, original),
                    new DiffChunk(DiffType.Insert, result)];

        var dp     = BuildDP(a, b);
        var chunks = Backtrack(a, b, dp);
        Merge(chunks);
        return chunks;
    }

    // Build the LCS table (bottom-right to top-left so backtracking goes forward).
    private static int[,] BuildDP(string[] a, string[] b)
    {
        int m = a.Length, n = b.Length;
        var dp = new int[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
            for (int j = n - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        return dp;
    }

    private static List<DiffChunk> Backtrack(string[] a, string[] b, int[,] dp)
    {
        var chunks = new List<DiffChunk>();
        int ai = 0, bi = 0, m = a.Length, n = b.Length;

        while (ai < m || bi < n)
        {
            if (ai < m && bi < n && a[ai] == b[bi])
            {
                chunks.Add(new DiffChunk(DiffType.Equal, a[ai++]));
                bi++;
            }
            else if (bi < n && (ai >= m || dp[ai, bi + 1] >= dp[ai + 1, bi]))
            {
                chunks.Add(new DiffChunk(DiffType.Insert, b[bi++]));
            }
            else
            {
                chunks.Add(new DiffChunk(DiffType.Delete, a[ai++]));
            }
        }
        return chunks;
    }

    // Merge consecutive same-type chunks so the caller gets fewer, larger spans.
    private static void Merge(List<DiffChunk> chunks)
    {
        for (int i = chunks.Count - 1; i > 0; i--)
        {
            if (chunks[i].Type == chunks[i - 1].Type)
            {
                chunks[i - 1] = new DiffChunk(chunks[i - 1].Type,
                                              chunks[i - 1].Text + chunks[i].Text);
                chunks.RemoveAt(i);
            }
        }
    }
}
