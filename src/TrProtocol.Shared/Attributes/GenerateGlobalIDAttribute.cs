using System;
using System.Collections.Generic;
using System.Text;

namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
    public class GenerateGlobalIDAttribute : Attribute
    {
    }
}
