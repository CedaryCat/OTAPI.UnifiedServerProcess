namespace TrProtocol.Interfaces;

public partial interface IBinarySerializable
{
    unsafe void ReadContent(ref void* ptr);
    unsafe void WriteContent(ref void* ptr);
}
