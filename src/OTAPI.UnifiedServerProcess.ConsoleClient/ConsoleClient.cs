using System.IO.Pipes;
using System.Text;

namespace OTAPI.UnifiedServerProcess.ConsoleClient
{
    public class ConsoleClient : IDisposable
    {
        private readonly NamedPipeClientStream _pipeClient;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        public ConsoleClient(string pipeName) {
            _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipeClient.Connect();
            _reader = new StreamReader(_pipeClient);
            _writer = new StreamWriter(_pipeClient) { AutoFlush = true };

            // start a thread to listen for commands
            var listenThread = new Thread(ListenForCommands);
            listenThread.IsBackground = true;
            listenThread.Start();
        }

        private void ListenForCommands() {
            try {
                while (_pipeClient.IsConnected) {

                    var command = _reader.ReadLine();
                    if (command == null) continue;

                    var parts = command.Split(new[] { ':' }, 2);
                    if (parts.Length < 2) continue;

                    var commandType = parts[0];
                    var arguments = parts[1];

                    switch (commandType) {
                        case "SET_BG_COLOR":
                            Console.BackgroundColor = (ConsoleColor)Enum.Parse(typeof(ConsoleColor), arguments);
                            break;
                        case "SET_FG_COLOR":
                            Console.ForegroundColor = (ConsoleColor)Enum.Parse(typeof(ConsoleColor), arguments);
                            break;
                        case "SET_INPUT_ENCODING":
                            Console.InputEncoding = Encoding.GetEncoding(arguments);
                            break;
                        case "SET_OUTPUT_ENCODING":
                            Console.OutputEncoding = Encoding.GetEncoding(arguments);
                            break;
                        case "SET_WINDOW_SIZE":
                            var size = arguments.Split(',');
                            Console.WindowWidth = int.Parse(size[0]);
                            Console.WindowHeight = int.Parse(size[1]);
                            break;
                        case "SET_WINDOW_POS":
                            var pos = arguments.Split(',');
                            Console.WindowLeft = int.Parse(pos[0]);
                            Console.WindowTop = int.Parse(pos[1]);
                            break;
                        case "SET_TITLE":
                            Console.Title = arguments;
                            break;
                        case "WRITE":
                            Console.Write(arguments);
                            break;
                        case "WRITE_LINE":
                            Console.WriteLine(arguments);
                            break;
                        case "WRITE_FORMAT":
                            var formatParts = arguments.Split(new[] { "|||" }, StringSplitOptions.None);
                            if (formatParts.Length > 1) {
                                var formatArgs = formatParts[1].Split(new[] { "||" }, StringSplitOptions.None);
                                Console.Write(formatParts[0], formatArgs);
                            }
                            else {
                                Console.Write(formatParts[0]);
                            }
                            break;
                        case "WRITE_LINE_FORMAT":
                            var lineFormatParts = arguments.Split(new[] { "|||" }, StringSplitOptions.None);
                            if (lineFormatParts.Length > 1) {
                                var lineFormatArgs = lineFormatParts[1].Split(new[] { "||" }, StringSplitOptions.None);
                                Console.WriteLine(lineFormatParts[0], lineFormatArgs);
                            }
                            else {
                                Console.WriteLine(lineFormatParts[0]);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error in client: {ex.Message}");
            }
            Environment.Exit(0);
        }

        public void SendInput(string input) {
            _writer.WriteLine($"INPUT:{input}");
        }

        public void Dispose() {
            _pipeClient?.Dispose();
        }
    }
}
