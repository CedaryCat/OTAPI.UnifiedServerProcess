namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ConditionLookupMatchAttribute : Attribute
    {
        public string LookupTable;
        public string LookupKeyField;
        public ConditionLookupMatchAttribute(string lookupTable, string lookupKeyField) {
            LookupTable = lookupTable;
            LookupKeyField = lookupKeyField;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ConditionLookupNotMatchAttribute : Attribute
    {
        public string LookupTable;
        public string LookupKeyField;
        public ConditionLookupNotMatchAttribute(string lookupTable, string lookupKeyField) {
            LookupTable = lookupTable;
            LookupKeyField = lookupKeyField;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ConditionLookupEqualAttribute : Attribute
    {
        public string LookupTable;
        public string LookupKeyField;
        public object Predicate;
        public ConditionLookupEqualAttribute(string lookupTable, string lookupKeyField, object pred) {
            LookupTable = lookupTable;
            LookupKeyField = lookupKeyField;
            Predicate = pred;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ConditionLookupNotEqualAttribute : Attribute
    {
        public string LookupTable;
        public string LookupKeyField;
        public object Predicate;
        public ConditionLookupNotEqualAttribute(string lookupTable, string lookupKeyField, object pred) {
            LookupTable = lookupTable;
            LookupKeyField = lookupKeyField;
            Predicate = pred;
        }
    }
}
