using System;
using coder.net.core.common;

namespace coder.net.core.pubsub.messages
{
    public class DataMessage : Message
    {
        public Memory<byte> Payload { get; private set; }

        public DataMessage(Id messageId, Memory<byte> data)
            : base(messageId)
        {
            Payload = data;
        }
    }
}
