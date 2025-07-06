namespace TrProtocol.Attributes {
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class PolymorphicBaseAttribute : Attribute {
        public readonly Type EnumIdentity;
        public readonly string IdentityName;
        public PolymorphicBaseAttribute(Type enumIdentity, string identityPropName) {
            EnumIdentity = enumIdentity;
            IdentityName = identityPropName;
        }
    }
}
