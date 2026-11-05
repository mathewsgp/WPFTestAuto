using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WpfTestIde.Uia3Worker.Core.Protocols;

namespace WpfTestIde.Uia3Worker.Core;

/// <summary>
/// Named pipe server for the UIA3 worker. Accepts JSON commands from the
/// Python driver, dispatches to the Uia3Probe, and sends JSON responses.
/// </summary>
public sealed class Uia3NamedPipeServer : IDisposable
{
    private readonly Uia3Probe _probe;
    private readonly string _pipeName;
    private NamedPipeServerStream? _server;
    private Task? _listenerTask;
    private CancellationTokenSource _cts = new();
    private bool _disposed;

    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public Uia3NamedPipeServer(Uia3Probe probe, string pipeName)
    {
        _probe = probe;
        _pipeName = pipeName;
    }

    public void Start()
    {
        _server = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);

        _listenerTask = Task.Run(ListenLoop, _cts.Token);
    }

    private async Task ListenLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _server?.WaitForConnection();
                ClientConnected?.Invoke();

                while (!_cts.Token.IsCancellationRequested)
                {
                    string? request = await ReadMessageAsync(_server!);
                    if (request == null)
                        break;

                    var response = Dispatch(request);
                    await WriteMessageAsync(_server!, response);
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            finally
            {
                ClientDisconnected?.Invoke();
                try { _server?.Dispose(); } catch { }
                _server = null;

                if (!_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, _cts.Token);
                    try
                    {
                        _server = new NamedPipeServerStream(
                            _pipeName,
                            PipeDirection.InOut,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Message,
                            PipeOptions.Asynchronous);
                    }
                    catch
                    {
                        _server = null;
                    }
                }
            }
        }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<string?> ReadMessageAsync(NamedPipeServerStream stream)
    {
        var sb = new StringBuilder();
        var buffer = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0)
                return sb.Length > 0 ? sb.ToString() : null;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (sb.ToString().EndsWith("\n"))
                return sb.ToString()[..^1];
        }
    }

    private static async Task WriteMessageAsync(NamedPipeServerStream stream, string message)
    {
        var data = Encoding.UTF8.GetBytes(message + "\n");
        await stream.WriteAsync(data, 0, data.Length);
        await stream.FlushAsync();
    }

    private string Dispatch(string request)
    {
        try
        {
            var req = JsonSerializer.Deserialize<WorkerRequest>(request, _jsonOpts);
            if (req == null)
                return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Invalid request" });

            var resp = new WorkerResponse { Success = true };

            switch (req.Command)
            {
                case "find_element":
                    resp.Data = new { elementId = _probe.FindElement(req.SearchBy!, req.Value!, req.Index) };
                    break;
                case "find_elements":
                    resp.Data = new { elementIds = _probe.FindElements(req.SearchBy!, req.Value!) };
                    break;
                case "invoke":
                    resp.Data = new { success = _probe.Invoke(req.ElementId!.Value) };
                    break;
                case "set_value":
                    resp.Data = new { success = _probe.SetValue(req.ElementId!.Value, req.Value!) };
                    break;
                case "get_text":
                    resp.Data = new { text = _probe.GetText(req.ElementId!.Value) };
                    break;
                case "toggle":
                    resp.Data = new { success = _probe.Toggle(req.ElementId!.Value, req.State) };
                    break;
                case "double_click":
                    resp.Data = new { success = _probe.DoubleClick(req.ElementId!.Value) };
                    break;
                case "right_click":
                    resp.Data = new { success = _probe.RightClick(req.ElementId!.Value) };
                    break;
                case "press_keys":
                    resp.Data = new { success = _probe.PressKeys(req.ElementId!.Value, req.Keys!) };
                    break;
                case "drag_drop":
                    resp.Data = new { success = _probe.DragDrop(req.SrcElementId!.Value, req.DstElementId!.Value) };
                    break;
                case "hover":
                    resp.Data = new { success = _probe.Hover(req.ElementId!.Value) };
                    break;
                case "scroll":
                    resp.Data = new { success = _probe.Scroll(req.ElementId!.Value, req.Direction!) };
                    break;
                case "get_attribute":
                    resp.Data = new { value = _probe.GetAttribute(req.ElementId!.Value, req.AttributeName!) };
                    break;
                case "is_visible":
                    resp.Data = new { visible = _probe.IsVisible(req.ElementId!.Value) };
                    break;
                case "is_enabled":
                    resp.Data = new { enabled = _probe.IsEnabled(req.ElementId!.Value) };
                    break;
                case "capture_screenshot":
                    resp.Data = new { data = _probe.CaptureScreenshot(req.ElementId) };
                    break;
                case "get_data_grid_content_ocr":
                    resp.Data = new { content = _probe.GetDataGridContent(req.ElementId!.Value) };
                    break;
                case "attach":
                    resp.Data = new { connected = true };
                    break;
                case "close":
                    _probe.Close();
                    resp.Data = new { success = true };
                    break;
                default:
                    resp.Success = false;
                    resp.Error = $"Unknown command: {req.Command}";
                    break;
            }

            return JsonSerializer.Serialize(resp, _jsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _server?.Disconnect(); } catch { }
        try { _server?.Dispose(); } catch { }
        _server = null;
        _listenerTask?.Wait(5000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
    }
}
