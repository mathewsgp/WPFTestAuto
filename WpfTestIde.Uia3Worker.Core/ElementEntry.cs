using System;

namespace WpfTestIde.Uia3Worker.Core;

/// <summary>
/// Cached element entry: an AutomationElement plus the search criteria used to
/// locate it, so stale elements can be re-resolved on action commands.
/// </summary>
public sealed class ElementEntry
{
    public int ElementId { get; set; }
    public System.Windows.Automation.AutomationElement Element { get; set; } = null!;
    public string SearchBy { get; set; } = "";
    public string SearchValue { get; set; } = "";
    public int? Index { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
}
