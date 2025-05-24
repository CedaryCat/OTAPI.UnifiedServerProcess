using ModFramework;
using System;
using System.IO;
using System.Text;

[Modification(ModType.PreRead, "Add RootContext", ModPriority.Early)]
[MonoMod.MonoModIgnore]
void PatchTileProvider(ModFwModder modder) {
    Console.WriteLine(modder.Module.GetType("UnifiedServerProcess.RootContext").FullName);
}

namespace UnifiedServerProcess {
    public class RootContext {
        public readonly string Name;
        public ConsoleSystemContext Console;
        public RootContext(string name) {
            Name = name;

            Console = new ConsoleSystemContext(this);
        }
    }
    public class ConsoleSystemContext(RootContext root) : IDisposable {
        public readonly RootContext root = root;
        protected virtual void Dispose(bool disposing) { }
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #region Methods Implementation
        public virtual void Write(ulong value) => Console.Write(value);
        public virtual void Write(bool value) => Console.Write(value);
        public virtual void Write(char value) => Console.Write(value);
        public virtual void Write(char[]? buffer) => Console.Write(buffer);
        public virtual void Write(int value) => Console.Write(value);
        public virtual void Write(long value) => Console.Write(value);
        public virtual void Write(decimal value) => Console.Write(value);
        public virtual void Write(object? value) => Console.Write(value);
        public virtual void Write(float value) => Console.Write(value);
        public virtual void Write(string? value) => Console.Write(value);
        public virtual void Write(string format, object? arg0) => Console.Write(format, arg0);
        public virtual void Write(string format, object? arg0, object? arg1) => Console.Write(format, arg0, arg1);
        public virtual void Write(string format, object? arg0, object? arg1, object? arg2) => Console.Write(format, arg0, arg1, arg2);
        public virtual void Write(string format, params object?[]? arg) => Console.Write(format, arg);
        public virtual void Write(uint value) => Console.Write(value);
        public virtual void Write(char[] buffer, int index, int count) => Console.Write(buffer, index, count);
        public virtual void Write(double value) => Console.Write(value);
        public virtual void WriteLine(uint value) => Console.WriteLine(value);
        public virtual void WriteLine() => Console.WriteLine();
        public virtual void WriteLine(bool value) => Console.WriteLine(value);
        public virtual void WriteLine(char[]? buffer) => Console.WriteLine(buffer);
        public virtual void WriteLine(char[] buffer, int index, int count) => Console.WriteLine(buffer, index, count);
        public virtual void WriteLine(decimal value) => Console.WriteLine(value);
        public virtual void WriteLine(double value) => Console.WriteLine(value);
        public virtual void WriteLine(int value) => Console.WriteLine(value);
        public virtual void WriteLine(long value) => Console.WriteLine(value);
        public virtual void WriteLine(object? value) => Console.WriteLine(value);
        public virtual void WriteLine(float value) => Console.WriteLine(value);
        public virtual void WriteLine(string? value) => Console.WriteLine(value);
        public virtual void WriteLine(string format, object? arg0) => Console.WriteLine(format, arg0);
        public virtual void WriteLine(string format, object? arg0, object? arg1) => Console.WriteLine(format, arg0, arg1);
        public virtual void WriteLine(string format, object? arg0, object? arg1, object? arg2) => Console.WriteLine(format, arg0, arg1, arg2);
        public virtual void WriteLine(string format, params object?[]? arg) => Console.WriteLine(format, arg);
        public virtual void WriteLine(ulong value) => Console.WriteLine(value);
        public virtual void WriteLine(char value) => Console.WriteLine(value);
        public virtual void Clear() => Console.Clear();
        public virtual string? ReadLine() => Console.ReadLine();
        public virtual int Read() => Console.Read();
        public virtual ConsoleKeyInfo ReadKey() => Console.ReadKey();
        public virtual ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

        #endregion

        #region Properties Implementation
        public virtual ConsoleColor BackgroundColor {
            get => Console.BackgroundColor;
            set => Console.BackgroundColor = value;
        }
        public virtual ConsoleColor ForegroundColor {
            get => Console.ForegroundColor;
            set => Console.ForegroundColor = value;
        }
        public virtual Encoding InputEncoding {
            get => Console.InputEncoding;
            set => Console.InputEncoding = value;
        }
        public virtual Encoding OutputEncoding {
            get => Console.OutputEncoding;
            set => Console.OutputEncoding = value;
        }
        public virtual int WindowHeight {
            get => Console.WindowHeight;
            set => Console.WindowHeight = value;
        }
        public virtual int WindowLeft {
            get => Console.WindowLeft;
            set => Console.WindowLeft = value;
        }
        public virtual int WindowTop {
            get => Console.WindowTop;
            set => Console.WindowTop = value;
        }
        public virtual int WindowWidth {
            get => Console.WindowWidth;
            set => Console.WindowWidth = value;
        }
        public virtual TextWriter Out => Console.Out;
        public virtual TextWriter Error => Console.Error;
        public virtual TextReader In => Console.In;
        public virtual string Title {
            get => Console.Title;
            set => Console.Title = value;
        }
        #endregion
    }
}
