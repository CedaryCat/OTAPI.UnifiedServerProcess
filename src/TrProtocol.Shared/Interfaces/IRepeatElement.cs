using System;
using System.Collections.Generic;
using System.Text;

namespace TrProtocol.Interfaces
{
    public interface IRepeatElement<TCount> : IBinarySerializable where TCount : unmanaged, IConvertible
    {
        public TCount RepeatCount { get; set; }
    }
}
