namespace TrProtocol.Interfaces
{
    public interface ISerializableView<TView> where TView : unmanaged, IPackedSerializable
    {
        TView View { get; set; }
    }
}
