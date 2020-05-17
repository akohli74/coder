using System;
using coder.net.core.common;

namespace coder.net.core.pubsub.messages
{
    public class ErrorMessage : Message
    {
        public Exception Error { get; private set; }

        public ErrorMessage(Id messageId, Exception ex)
            : base(messageId)
        {
            Error = ex;
        }
    }
}
