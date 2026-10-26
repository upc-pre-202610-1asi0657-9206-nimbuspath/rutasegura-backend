using System.Linq.Expressions;

namespace Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

public static class QueryableExtensions
{
    /// <summary>
    ///     <c>WHERE key = k1 OR key = k2 …</c>. The MySQL provider cannot translate <c>list.Contains(x.Key)</c> when the
    ///     key is a value object with a converter (or a parameter collection), so the filter is built as an OR of
    ///     constants (same approach as Trip &amp; Tracking's repositories). An empty list matches nothing.
    /// </summary>
    public static IQueryable<T> WhereIn<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key,
        IEnumerable<TKey> values)
    {
        var distinct = values.Distinct().ToList();
        if (distinct.Count == 0) return query.Where(_ => false);

        var body = distinct
            .Select(value => (Expression)Expression.Equal(key.Body, Expression.Constant(value, typeof(TKey))))
            .Aggregate(Expression.OrElse);
        return query.Where(Expression.Lambda<Func<T, bool>>(body, key.Parameters));
    }
}
