namespace PortAtlas.Core.Export;

/// <summary>
/// End of a flow whose audit log recorded it.
/// </summary>
public enum FlowObservationSide
{
    /// <summary>
    /// Only the log of the source, which opened the connection, recorded the flow.
    /// </summary>
    Source,

    /// <summary>
    /// Only the log of the destination, which accepted the connection, recorded the flow.
    /// </summary>
    Destination,

    /// <summary>
    /// The logs of both ends recorded the flow.
    /// </summary>
    Both
}
