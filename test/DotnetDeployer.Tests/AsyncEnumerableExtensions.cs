namespace DotnetDeployer.Tests;

public static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }

    public static async Task<IEnumerable<Result>> MapEach<T>(this IAsyncEnumerable<Result<T>> source, Func<T, Task<Result>> action)
    {
        var list = new List<Result>();
        await foreach (var item in source)
        {
            var result = await item.Bind(action);
            list.Add(result);
        }
        return list;
    }
}
