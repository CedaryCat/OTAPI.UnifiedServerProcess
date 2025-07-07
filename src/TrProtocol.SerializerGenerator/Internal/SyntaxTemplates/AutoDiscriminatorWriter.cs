using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.SyntaxTemplates
{
    public static class AutoDiscriminatorWriter
    {
        public static void WriteAutoDiscriminator(this BlockNode classBlock, ProtocolTypeData model) {
            foreach (var (enumType, identityName, value) in model.AutoDiscriminators) {
                classBlock.WriteLine($"public {(model.IsValueType ? "readonly " : "")}{enumType.Name} {identityName} => {value};");
            }
        }
    }
}
