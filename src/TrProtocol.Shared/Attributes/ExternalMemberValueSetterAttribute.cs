﻿namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ExternalMemberValueAttribute : Attribute
    {
        public readonly string MemberName;
        public readonly object DefaultValue;
        public ExternalMemberValueAttribute(string member, object value) {
            MemberName = member;
            DefaultValue = value;
        }
    }
}
