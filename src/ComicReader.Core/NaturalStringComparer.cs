using System.Globalization;

namespace ComicReader.Core;

/// <summary>
/// Compares strings chunk-wise so that "page2" sorts before "page10",
/// which is the natural order expected for comic pages.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            var cx = x[i];
            var cy = y[j];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var nx = x.AsSpan(si, i - si).TrimStart('0');
                var ny = y.AsSpan(sj, j - sj).TrimStart('0');

                if (nx.Length != ny.Length) return nx.Length - ny.Length;
                var numCmp = nx.SequenceCompareTo(ny);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                var cmp = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (cmp != 0) return cmp;
                i++;
                j++;
            }
        }

        return (x.Length - i) - (y.Length - j);
    }
}
