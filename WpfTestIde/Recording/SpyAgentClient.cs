using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WpfTestIde.Recording
{
    public class SpyAgentResponse
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// C# Named Pipe client for the WpfSpyAgent — the IDE's own copy of
    /// the same protocol the Python WPFSpyRealDriver uses (see
    /// docs/PROTOCOL.md). Used both by the recorder (ProbeAt, GetText) and
    /// potentially by a future "run from the IDE against a live app" mode.
    /// A new connection is opened per call, matching the reference Python
    /// client's approach — simple and robust to the target app restarting
    /// between calls.
    /// </summary>
    public class SpyAgentClient
    {
        private readonly string _pipeName;

        public SpyAgentClient(string pipeName = "WPFSpyAgentPipe")
        {
            _pipeName = pipeName;
        }

        public SpyAgentResponse Send(string command, string? name = null, string? value = null, int? x = null, int? y = null, string? xpath = null, int? width = null, int? height = null, string? attributeName = null, string? targetName = null, string? targetXPath = null, string? automationId = null, int timeoutMs = 5000)
        {
            NamedPipeClientStream? pipe = null;
            StreamWriter? writer = null;
            StreamReader? reader = null;
            try
            {
                pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                pipe.Connect(2000);

                var request = new Dictionary<string, object?> { ["command"] = command };
                if (name != null) request["name"] = name;
                if (value != null) request["value"] = value;
                if (x != null) request["x"] = x;
                if (y != null) request["y"] = y;
                if (xpath != null) request["xpath"] = xpath;
                if (width != null) request["width"] = width;
                if (height != null) request["height"] = height;
                if (attributeName != null) request["attributeName"] = attributeName;
                if (targetName != null) request["targetName"] = targetName;
                if (targetXPath != null) request["targetXPath"] = targetXPath;
                if (automationId != null) request["automationId"] = automationId;

                writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };
                reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);

                string requestJson = JsonSerializer.Serialize(request);
                writer.WriteLine(requestJson);

                var readTask = System.Threading.Tasks.Task.Run(() => reader.ReadLine());
                if (!readTask.Wait(TimeSpan.FromMilliseconds(timeoutMs)))
                {
                    return new SpyAgentResponse { Success = false, Error = $"Agent response timeout after {timeoutMs}ms" };
                }

                string? line = readTask.Result;
                if (line is null)
                {
                    return new SpyAgentResponse { Success = false, Error = "No response from agent (null line)" };
                }

                var result = JsonSerializer.Deserialize<SpyAgentResponse>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (result is null)
                {
                    return new SpyAgentResponse { Success = false, Error = $"Malformed response (null after deserialize) | raw: [{line}]" };
                }
                if (!result.Success && string.IsNullOrEmpty(result.Error))
                {
                    return new SpyAgentResponse { Success = false, Error = $"unknown error | raw: [{line}]" };
                }
                return result;
            }
            catch (Exception ex)
            {
                return new SpyAgentResponse { Success = false, Error = $"Pipe exception: {ex.GetType().Name}: {ex.Message}" };
            }
            finally
            {
                writer?.Dispose();
                reader?.Dispose();
                pipe?.Dispose();
            }
        }
    }
}
