using System;

namespace coder.net.core.common
{
	public class Id : IEquatable<Id>
	{
		public Guid Identifier
		{
			get
			{
				if (_id == null)
				{
					_id = Guid.NewGuid();
				}

				return _id;
			}
		}

		private Guid _id;

		public Id()
		{
			_id = Guid.NewGuid();
		}

		public Id(Guid id)
		{
			_id = id;
		}

		public override bool Equals(object identifier)
		{
			if (!(identifier is Id))
			{
				return false;
			}

			var id = identifier as Id;
			return id.Identifier.Equals(Identifier);
		}

		public bool Equals(Id other)
		{
			return Identifier.Equals(other.Identifier);
		}

        public override string ToString()
        {
			return _id.ToString();
        }

        public override int GetHashCode()
		{
			return Identifier.GetHashCode();
		}
	}
}
