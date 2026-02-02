using System;

namespace OTAPI.UnifiedServerProcess.Extensions;

public static class EnumExt
{
    public unsafe static TEnum Remove<TEnum>(this TEnum value, TEnum flag) where TEnum : unmanaged, Enum
    {
        if (sizeof(TEnum) is 1) {
            var result = (byte)(*(byte*)&value & ~*(byte*)&flag);
            return *(TEnum*)&result;
        }
        else if (sizeof(TEnum) is 2) {
            var result = (ushort)(*(ushort*)&value & ~*(ushort*)&flag);
            return *(TEnum*)&result;
        }
        else if (sizeof(TEnum) is 4) {
            var result = (uint)(*(uint*)&value & ~*(uint*)&flag);
            return *(TEnum*)&result;
        }
        else if (sizeof(TEnum) is 8) {
            var result = (ulong)(*(ulong*)&value & ~*(ulong*)&flag);
            return *(TEnum*)&result;
        }
        else {
            throw new NotSupportedException("Unsupported enum size.");
        }
    }
}
