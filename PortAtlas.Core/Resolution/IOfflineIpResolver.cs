using System.Net;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Batch resolution of one IP address into a cache entry (reverse DNS, then registry owner).
/// </summary>
public interface IOfflineIpResolver
{
    /// <summary>
    /// Resolves <paramref name="address"/>.
    /// </summary>
    /// <param name="address">Address to resolve.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The entry describing the outcome; its host name is empty when nothing was found.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="address"/> is null.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/> is cancelled.</exception>
    Task<DnsCacheEntry> ResolveSingleAsync(IPAddress address, CancellationToken cancellationToken = default);
}
