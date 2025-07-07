namespace TrProtocol.Interfaces
{
    /// <summary>
    /// Indicates that the implementing type requires side-specific (client/server) handling during packet serialization/deserialization.
    /// Source generators use this interface to generate conditional parsing logic based on the execution environment.
    /// </summary>
    public interface ISideSpecific
    {
        /// <summary>
        /// Indicates whether the current execution context is server-side.
        /// This value is set by source-generated parsers and used with <see cref="Attributes.ConditionAttribute"/> 
        /// to determine which members participate in serialization/deserialization.
        /// </summary>
        public bool IsServerSide { get; set; }
    }
}
