using System.Diagnostics;

using System.Data.Common;

namespace Squeel;

public static class SqueelExtensions
{
    public static Task<IEnumerable<T>> QueryAsync<T>(
        this DbConnection connection,
        ref SqueelInterpolatedStringHandler query,
        CancellationToken ct = default)
    {
        throw new UnreachableException("This call failed to be intercepted by the Squeel source generator");
    }

    public static Task<int> ExecuteAsync(
        this DbConnection connection,
        ref SqueelInterpolatedStringHandler query,
        CancellationToken ct = default
    )
    {
        throw new UnreachableException("This call failed to be intercepted by the Squeel source generator");
    }
}