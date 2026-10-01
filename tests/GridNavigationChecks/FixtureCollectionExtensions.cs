using System.Collections.Generic;

internal static class FixtureCollectionExtensions
{
    public static void AddRange<T>(this IList<T> collection, IEnumerable<T> items)
    {
        foreach (T item in items) collection.Add(item);
    }
}
