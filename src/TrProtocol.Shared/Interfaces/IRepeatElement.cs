namespace TrProtocol.Interfaces;

public interface IRepeatElement<TCount> : IBinarySerializable where TCount : unmanaged, IConvertible
{
    public TCount RepeatCount { get; set; }
}
