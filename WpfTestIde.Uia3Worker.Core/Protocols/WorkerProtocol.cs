namespace WpfTestIde.Uia3Worker.Core.Protocols;

/// <summary>
/// JSON request/response contract between the Python Uia3Driver and the
/// C# Uia3Worker over a named pipe.
/// </summary>
public sealed class WorkerRequest
{
    public string Command { get; set; } = "";

    // find_element / find_elements
    public string? SearchBy { get; set; }
    public string? Value { get; set; }
    public int? Index { get; set; }

    // action commands
    public int? ElementId { get; set; }
    public bool? State { get; set; }          // toggle
    public string? Keys { get; set; }         // press_keys
    public string? Direction { get; set; }    // scroll
    public string? AttributeName { get; set; } // get_attribute

    // drag_drop
    public int? SrcElementId { get; set; }
    public int? DstElementId { get; set; }

    // capture_screenshot (optional element)
    public int? ScreenshotElementId { get; set; }
}

public sealed class WorkerResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}
