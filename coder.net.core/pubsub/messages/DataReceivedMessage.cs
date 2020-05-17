using System;
using coder.net.core.common;

namespace coder.net.core.pubsub.messages
{
    public class DataReceivedMessage : Message
    {
        public DateTime ReceivedAt { get; private set; }

        public DataReceivedMessage(Id messageId)
            : base(messageId)
        {
            ReceivedAt = DateTime.UtcNow;
        }
    }
}
