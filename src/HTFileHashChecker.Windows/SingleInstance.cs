using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HTFileHashChecker.Windows
{
    /// <summary>
    /// Keeps one window per user session. When Explorer starts the program once per selected file, later instances
    /// send their arguments to the first one over a named pipe that only the current user can open, then exit.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private const int MaxMessageBytes = 4 * 1024 * 1024;
        private const int MaxArguments = 10000;

        private readonly string mutexName;
        private readonly string pipeName;
        private Mutex mutex;
        private CancellationTokenSource listenCts;
        private Task listenTask;

        public SingleInstance(string applicationId)
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            int session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
            mutexName = "Local\\" + applicationId + "." + sid;
            pipeName = applicationId + "." + sid + "." + session;
        }

        public string PipeName { get { return pipeName; } }

        /// <summary>True when this process is the first instance for the user session.</summary>
        public bool TryBecomePrimary()
        {
            bool created;
            mutex = new Mutex(true, mutexName, out created);
            if (!created)
            {
                mutex.Dispose();
                mutex = null;
            }
            return created;
        }

        /// <summary>Starts receiving arguments from later instances. <paramref name="onArguments"/> runs on a
        /// background thread; marshal to the UI thread inside it.</summary>
        public void StartListening(Action<string[]> onArguments)
        {
            if (onArguments == null) throw new ArgumentNullException("onArguments");
            listenCts = new CancellationTokenSource();
            var token = listenCts.Token;
            // A dedicated thread (not the thread pool): the pipe must be listening at once, even when the pool is busy,
            // because Explorer starts one process per selected file and they all forward their arguments here.
            listenTask = Task.Factory.StartNew(() => ListenLoop(onArguments, token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void ListenLoop(Action<string[]> onArguments, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var server = CreateServer())
                    {
                        var wait = server.BeginWaitForConnection(null, null);
                        WaitHandle.WaitAny(new[] { wait.AsyncWaitHandle, token.WaitHandle });
                        if (token.IsCancellationRequested) return;
                        server.EndWaitForConnection(wait);
                        string[] args = ReadMessage(server);
                        if (args != null && args.Length > 0) onArguments(args);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException
                    || ex is DecoderFallbackException)
                {
                    // Malformed message or the pipe could not be created: back off briefly instead of spinning.
                    token.WaitHandle.WaitOne(250);
                }
                catch (ObjectDisposedException) { return; }
            }
        }

        private NamedPipeServerStream CreateServer()
        {
            var security = new PipeSecurity();
            var user = WindowsIdentity.GetCurrent().User;
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.SetOwner(user);
            return new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                64 * 1024, 64 * 1024, security);
        }

        /// <summary>Sends arguments to the primary instance; retries briefly while it starts up.</summary>
        public bool SendToPrimary(string[] args, int timeoutMilliseconds)
        {
            byte[] message = Encode(args);
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None))
                    {
                        int remaining = (int)Math.Max(100, (deadline - DateTime.UtcNow).TotalMilliseconds);
                        client.Connect(Math.Min(remaining, 1000));
                        client.Write(message, 0, message.Length);
                        client.Flush();
                        client.WaitForPipeDrain();
                        return true;
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) { return false; }
            }
            return false;
        }

        internal static byte[] Encode(string[] args)
        {
            string joined = string.Join("\0", args ?? new string[0]);
            byte[] payload = Encoding.UTF8.GetBytes(joined);
            if (payload.Length > MaxMessageBytes) throw new ArgumentException("Too many arguments.");
            var message = new byte[4 + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(message, 0);
            payload.CopyTo(message, 4);
            return message;
        }

        internal static string[] ReadMessage(Stream stream)
        {
            var header = ReadExactly(stream, 4);
            int length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > MaxMessageBytes) throw new InvalidDataException("Message too large.");
            var payload = ReadExactly(stream, length);
            string text = new UTF8Encoding(false, true).GetString(payload);
            var parts = text.Length == 0 ? new string[0] : text.Split('\0');
            if (parts.Length > MaxArguments) throw new InvalidDataException("Too many arguments.");
            return parts;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new InvalidDataException("Incomplete message.");
                offset += read;
            }
            return buffer;
        }

        public void Dispose()
        {
            if (listenCts != null)
            {
                listenCts.Cancel();
                try { if (listenTask != null) listenTask.Wait(2000); }
                catch (AggregateException) { }
                listenCts.Dispose();
                listenCts = null;
            }
            if (mutex != null)
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
                mutex.Dispose();
                mutex = null;
            }
        }
    }

    /// <summary>Parses the program's command line.</summary>
    public sealed class CommandLine
    {
        private CommandLine()
        {
            Paths = new List<string>();
            Unknown = new List<string>();
        }

        public List<string> Paths { get; private set; }
        public List<string> Unknown { get; private set; }
        public bool Calculate { get; private set; }
        public bool RegisterExplorer { get; private set; }
        public bool UnregisterExplorer { get; private set; }
        public string SelfTestOutput { get; private set; }

        public static CommandLine Parse(string[] args)
        {
            var c = new CommandLine();
            if (args == null) return c;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrWhiteSpace(a)) continue;
                if (a.StartsWith("--", StringComparison.Ordinal) || (a.StartsWith("/", StringComparison.Ordinal) && a.Length > 1 && !a.Contains("\\")))
                {
                    string name = a.TrimStart('-', '/').ToLowerInvariant();
                    switch (name)
                    {
                        case "calculate": c.Calculate = true; break;
                        case "register-explorer": c.RegisterExplorer = true; break;
                        case "unregister-explorer": c.UnregisterExplorer = true; break;
                        case "selftest":
                            if (i + 1 < args.Length) c.SelfTestOutput = args[++i];
                            break;
                        default: c.Unknown.Add(a); break;
                    }
                    continue;
                }
                c.Paths.Add(a);
            }
            return c;
        }
    }
}
