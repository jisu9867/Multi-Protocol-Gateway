using System.Threading.Channels;

namespace Gateway.Core.Common;

/// <summary>
/// Channel creation options for backpressure control
/// </summary>
public sealed class ChannelOptions
{
    /// <summary>
    /// Maximum capacity of the channel (for bounded channels)
    /// </summary>
    public int Capacity { get; init; } = 1000;

    /// <summary>
    /// Overflow policy when channel is full
    /// </summary>
    public BoundedChannelFullMode FullMode { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>
    /// Whether to allow synchronous continuations
    /// </summary>
    public bool AllowSynchronousContinuations { get; init; } = false;

    /// <summary>
    /// Whether the channel is single reader
    /// </summary>
    public bool SingleReader { get; init; } = false;

    /// <summary>
    /// Whether the channel is single writer
    /// </summary>
    public bool SingleWriter { get; init; } = false;

    /// <summary>
    /// Create a bounded channel with these options
    /// </summary>
    public BoundedChannelOptions ToBoundedChannelOptions()
    {
        return new BoundedChannelOptions(Capacity)
        {
            FullMode = FullMode,
            AllowSynchronousContinuations = AllowSynchronousContinuations,
            SingleReader = SingleReader,
            SingleWriter = SingleWriter
        };
    }

    /// <summary>
    /// Create a bounded channel with these options
    /// </summary>
    public static Channel<T> CreateBoundedChannel<T>(ChannelOptions options)
    {
        return System.Threading.Channels.Channel.CreateBounded<T>(options.ToBoundedChannelOptions());
    }

    /// <summary>
    /// Default options with Wait policy (blocks when full)
    /// </summary>
    public static ChannelOptions Default => new();

    /// <summary>
    /// Options with Drop policy (drops oldest when full)
    /// </summary>
    public static ChannelOptions DropOldest => new()
    {
        FullMode = BoundedChannelFullMode.DropOldest
    };

    /// <summary>
    /// Options with DropNewest policy (drops newest when full)
    /// </summary>
    public static ChannelOptions DropNewest => new()
    {
        FullMode = BoundedChannelFullMode.DropNewest
    };
}

