using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using TrProtocol;
using TrProtocol.Interfaces;

namespace Terraria.Localization;
public partial class NetworkText : IBinarySerializable
{
    public enum Mode : byte
    {
        Literal,
        Formattable,
        LocalizationKey
    }

    public NetworkText[] _substitutions = [];
    public string _text;
    public Mode _mode;

    public NetworkText(string text, Mode mode) {
        _text = text;
        _mode = mode;
    }
    public NetworkText() {
        _text = string.Empty;
    }

    [MemberNotNull(nameof(_text))]
    public unsafe void ReadContent(ref void* ptr) {
        _mode = (Mode)Unsafe.Read<byte>(ptr);
        ptr = Unsafe.Add<byte>(ptr, 1);
        _text = CommonCode.ReadString(ref ptr);
        DeserializeSubstitutionList(ref ptr);
    }
    public unsafe void DeserializeSubstitutionList(ref void* ptr) {
        if (_mode != 0) {
            _substitutions = new NetworkText[Unsafe.Read<byte>(ptr)];
            ptr = Unsafe.Add<byte>(ptr, 1);
            for (int i = 0; i < _substitutions.Length; i++) {
                var text = _substitutions[i] = new();
                text.ReadContent(ref ptr);
            }
        }
    }
    public unsafe void WriteContent(ref void* ptr) {
        Unsafe.Write(ptr, (byte)_mode);
        ptr = Unsafe.Add<byte>(ptr, 1);
        CommonCode.WriteString(ref ptr, _text);
        SerializeSubstitutionList(ref ptr);
    }
    public unsafe void SerializeSubstitutionList(ref void* ptr) {
        if (_mode != 0) {
            Unsafe.Write(ptr, (byte)_substitutions.Length);
            ptr = Unsafe.Add<byte>(ptr, 1);
            for (int i = 0; i < _substitutions.Length; i++) {
                _substitutions[i].WriteContent(ref ptr);
            }
        }
    }
}
