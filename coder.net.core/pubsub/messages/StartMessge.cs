using System;
using coder.net.core.common;

namespace coder.net.core.pubsub.messages
{
    public class StartMessage : Message
    {
        public bool Start { get; private set; }

        public StartMessage(Id messageId, bool start)
            : base(messageId)
        {
            Start = start;
        }
    }
}
