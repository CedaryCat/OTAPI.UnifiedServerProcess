namespace TrProtocol.Attributes {
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public class ImplementationClaimAttribute : Attribute {
        public readonly Enum ImplementationIdentity;
        public ImplementationClaimAttribute(object implIdentity) {
            ImplementationIdentity = (Enum)implIdentity;
        }
    }
}
