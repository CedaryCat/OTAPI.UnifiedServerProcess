namespace TrProtocol.Exceptions
{
    public class UnknownDiscriminatorException : Exception
    {
        public UnknownDiscriminatorException(Type baseType, Enum id, long value)
            : base($"Unknown {baseType.Name} subtype id '{id}' ({value}) encountered during deserialization.") {
        }
    }
}
