using System;

namespace OTAPI.UnifiedServerProcess.Extensions;

public static class EnumExt
{
    public static unsafe TEnum Remove<TEnum>(this TEnum value, TEnum flag) where TEnum : unmanaged, Enum {
        if (sizeof(TEnum) is 1) {
            byte result = (byte)(*(byte*)&value & ~*(byte*)&flag);
            return *(TEnum*)&result;
        }
        else if (sizeof(TEnum) is 2) {
            ushort result = (ushort)(*(ushort*)&value & ~*(ushort*)&flag);
            return *(TEnum*)&result;
        }
        else if (sizeof(TEnum) is 4) {
            uint result = (uint)(*(uint*)&value & ~*(uint*)&flag);
            return *(TEnum*)&result;
        }
        else if (sizeof(TEnum) is 8) {
            ulong result = (ulong)(*(ulong*)&value & ~*(ulong*)&flag);
            return *(TEnum*)&result;
        }
        else {
            throw new NotSupportedException("Unsupported enum size.");
        }
    }
}
