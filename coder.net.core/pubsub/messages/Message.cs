using System;
using coder.net.core.common;

namespace coder.net.core.pubsub.messages
{
	[Serializable]
	public class Message
	{
		public readonly Id MessageId;

		public Message(Id messageId)
		{
			MessageId = messageId;
		}
	}
}
