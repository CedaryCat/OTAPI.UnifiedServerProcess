﻿using System.IO.Compression;

namespace TrProtocol.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class CompressAttribute : Attribute
    {
        public readonly CompressionLevel Level;
        public readonly int BufferSize;
        public CompressAttribute(CompressionLevel level, int bufferSize) {
            Level = level;
            BufferSize = bufferSize;
        }
    }
}
