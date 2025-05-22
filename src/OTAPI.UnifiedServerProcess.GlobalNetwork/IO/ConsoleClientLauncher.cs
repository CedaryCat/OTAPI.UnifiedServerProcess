using OTAPI.UnifiedServerProcess.GlobalNetwork.Servers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using UnifiedServerProcess;

namespace OTAPI.UnifiedServerProcess.GlobalNetwork.IO
{
    public class ConsoleClientLauncher : ConsoleSystemContext
    {
        private readonly string _pipeName;
        private NamedPipeServerStream _pipeServer;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private Process _clientProcess;
        [MemberNotNullWhen(false, nameof(_clientProcess), nameof(_pipeServer), nameof(_reader), nameof(_writer))]
        private bool IsRunning { get; set; } = true;
        private readonly Lock _syncLock = new();

        public ConsoleClientLauncher(ServerContext server) : base(server) {
            _pipeName = $"USP_Console_{server.Name}_{server.UniqueId}";
            RestartCommunication();
        }
        [MemberNotNull(nameof(_pipeServer))]
        private void InitializePipeServer() {
            lock (_syncLock) {
                // 清理旧管道资源
                _pipeServer?.Dispose();
                _reader = null;
                _writer = null;

                // 创建新管道服务器
                _pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous
                );

                // 异步等待连接
                _pipeServer.WaitForConnection();

                try {
                    _reader = new StreamReader(_pipeServer);
                    _writer = new StreamWriter(_pipeServer) { AutoFlush = true };
                    StartListeningThread();
                }
                catch (ObjectDisposedException) { /* 正常关闭时忽略 */ }
                catch {
                    if (IsRunning) RestartCommunication();
                }
            }
        }

        [MemberNotNull(nameof(_clientProcess))]
        private void StartClientProcess() {
            lock (_syncLock) {
                // 清理旧进程
                try {
                    if (_clientProcess != null && !_clientProcess.HasExited) {
                        _clientProcess.Kill();
                    }
                }
                catch { }

                // 启动新进程
                var clientExePath = "OTAPI.UnifiedServerProcess.ConsoleClient.exe";
                var startInfo = new ProcessStartInfo {
                    FileName = clientExePath,
                    Arguments = _pipeName,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                _clientProcess = new Process { StartInfo = startInfo };
                _clientProcess.EnableRaisingEvents = true;
                _clientProcess.Exited += (sender, args) => {
                    _reader = null;
                    _writer = null;
                    _pipeServer?.Dispose();
                    if (IsRunning) {
                        Thread.Sleep(1000);
                        RestartCommunication();
                    }
                };
                _clientProcess.Start();
            }
        }

        private void StartListeningThread() {
            var listenThread = new Thread(ListenForInput) {
                IsBackground = true
            };
            listenThread.Start();
        }

        private void ListenForInput() {
            try {
                while (IsRunning) {
                    if (!_pipeServer.IsConnected) {
                        Thread.Sleep(100);
                        continue;
                    }
                    var input = _reader?.ReadLine();
                    if (input == null) continue;

                    if (input.StartsWith("INPUT:")) {
                        var actualInput = input.Substring(6);
                        OnInputReceived?.Invoke(this, actualInput);
                    }
                }
            }
            catch {
                if (IsRunning) {
                    // 重新启动管道服务器和客户端进程
                    RestartCommunication();
                }
            }
        }

        [MemberNotNull(nameof(_clientProcess), nameof(_pipeServer))]
        private void RestartCommunication() {
            lock (_syncLock) {
                if (!IsRunning) return;

                try {
                    _pipeServer?.Dispose();
                    _reader = null;
                    _writer = null;

                    StartClientProcess();
                    InitializePipeServer();

                    if (!string.IsNullOrEmpty(cachedTitle)) {
                        Title = cachedTitle;
                    }
                }
                catch {
                    Thread.Sleep(3000);
                    RestartCommunication();
                }
            }
        }

        public event EventHandler<string>? OnInputReceived;

        public override void Dispose(bool disposing) {
            if (disposing) {
                IsRunning = false;

                try {
                    if (_clientProcess != null && !_clientProcess.HasExited) {
                        _clientProcess.Kill();
                    }
                    _clientProcess?.Dispose();
                }
                catch { }

                try {
                    _pipeServer?.Dispose();
                    _reader = null;
                    _writer = null;
                }
                catch { }
            }

            base.Dispose(disposing);
        }

        ConsoleColor cachedBackgroundColor = Console.BackgroundColor;
        ConsoleColor cachedForegroundColor = Console.ForegroundColor;
        Encoding cachedInputEncoding = Console.InputEncoding;
        Encoding cachedOutputEncoding = Console.OutputEncoding;
        int cachedWindowHeight = Console.WindowHeight;
        int cachedWindowLeft = Console.WindowLeft;
        int cachedWindowTop = Console.WindowTop;
        int cachedWindowWidth = Console.WindowWidth;
        string cachedTitle = "";

        public override ConsoleColor BackgroundColor {
            get => cachedBackgroundColor;
            set {
                cachedBackgroundColor = value;
                _writer?.WriteLine($"SET_BG_COLOR:{value}");
            }
        }

        public override ConsoleColor ForegroundColor {
            get => cachedForegroundColor;
            set {
                cachedForegroundColor = value;
                _writer?.WriteLine($"SET_FG_COLOR:{value}");
            }
        }

        public override Encoding InputEncoding {
            get => cachedInputEncoding;
            set {
                cachedInputEncoding = value;
                _writer?.WriteLine($"SET_INPUT_ENCODING:{value.WebName}");
            }
        }

        public override Encoding OutputEncoding {
            get => cachedOutputEncoding;
            set {
                cachedOutputEncoding = value;
                _writer?.WriteLine($"SET_OUTPUT_ENCODING:{value.WebName}");
            }
        }

        public override int WindowHeight {
            get => cachedWindowHeight;
            set {
                cachedWindowHeight = value;
                _writer?.WriteLine($"SET_WINDOW_SIZE:{WindowWidth},{value}");
            }
        }

        public override int WindowLeft {
            get => cachedWindowLeft;
            set {
                cachedWindowLeft = value;
                _writer?.WriteLine($"SET_WINDOW_POS:{value},{WindowTop}");
            }
        }

        public override int WindowTop {
            get => cachedWindowTop;
            set {
                cachedWindowTop = value;
                _writer?.WriteLine($"SET_WINDOW_POS:{WindowLeft},{value}");
            }
        }

        public override int WindowWidth {
            get => cachedWindowWidth;
            set {
                cachedWindowWidth = value;
                _writer?.WriteLine($"SET_WINDOW_SIZE:{value},{WindowHeight}");
            }
        }

        public override string Title {
            get => cachedTitle;
            set {
                cachedTitle = value;
                _writer?.WriteLine($"SET_TITLE:{value}");
            }
        }

        public override void Write(string? value) {
            if (value == null) return;
            _writer?.WriteLine($"WRITE:{EscapeNewLines(value)}");
        }

        public override void Write(string format, params object?[]? arg) {
            if (arg == null) {
                Write(format);
                return;
            }
            _writer?.WriteLine($"WRITE:{string.Format(EscapeNewLines(format), arg)}");
        }

        public override void WriteLine(string? value) {
            if (value == null) return;
            _writer?.WriteLine($"WRITE_LINE:{EscapeNewLines(value)}");
        }

        public override void WriteLine(string format, params object?[]? arg) {
            if (arg == null) {
                WriteLine(format);
                return;
            }
            _writer?.WriteLine($"WRITE_LINE:{string.Format(EscapeNewLines(format), arg)}");
        }

        private static string EscapeNewLines(string input) {
            return input.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
