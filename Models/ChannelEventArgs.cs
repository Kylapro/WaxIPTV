using System;

namespace WaxIPTV.Models
{
    /// <summary>
    /// Event arguments for channel activation events.  Wraps the activated
    /// <see cref="Channel"/> so that it can be passed through the
    /// <see cref="EventHandler{TEventArgs}"/> delegate which requires
    /// argument types derive from <see cref="EventArgs"/>.
    /// </summary>
    public sealed class ChannelEventArgs : EventArgs
    {
        /// <summary>
        /// Constructs a new instance of <see cref="ChannelEventArgs"/>.
        /// </summary>
        /// <param name="channel">The channel that was activated.</param>
        public ChannelEventArgs(Channel channel)
        {
            Channel = channel;
        }

        /// <summary>
        /// Gets the channel associated with this activation.
        /// </summary>
        public Channel Channel { get; }
    }
}