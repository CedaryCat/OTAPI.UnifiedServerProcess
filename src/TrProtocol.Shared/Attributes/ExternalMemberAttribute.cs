using System;
using System.Collections.Generic;
using System.Text;

namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ExternalMemberAttribute : Attribute
    {
    }
}
