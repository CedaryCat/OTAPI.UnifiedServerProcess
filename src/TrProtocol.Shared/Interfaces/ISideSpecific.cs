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
    /// <summary>
    /// Marker interface indicating that the type is not <see cref="ISideSpecific"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is automatically added by source generators to all types that do <b>not</b> implement
    /// <see cref="ISideSpecific"/>. Developers do <b>not</b> need to manually implement this interface.
    /// </para>
    /// <para>
    /// Its primary purpose is to enable generic constraints that effectively express
    /// <c>not ISideSpecific</c> in high-performance scenarios involving <c>struct</c>-based type specialization.
    /// This allows source generators or AOT-optimized code paths to distinguish between
    /// <see cref="ISideSpecific"/> and non-specific types at compile time.
    /// </para>
    /// </remarks>
    public interface INonSideSpecific { }

}
