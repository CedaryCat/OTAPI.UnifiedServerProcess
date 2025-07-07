using System;
using System.Collections.Generic;
using System.Text;

namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
    public class ExplicitImportTypeAttribute(Type type) : Attribute
    {
        public Type Type = type;
    }
}
