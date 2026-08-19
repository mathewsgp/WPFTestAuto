using System;

namespace WpfTestIde.Models
{
    public enum StepKind { Action, Verify, VerifyOcr, VerifyEnabled, VerifyVisible, VerifyContains, VerifyRegex, VerifyAttribute, WaitExists, WaitVisible, WaitEnabled, WaitTextContains, CheckpointProperty, CheckpointArea, CheckpointImage, CheckpointDataGrid, CheckpointCount, CheckpointAttribute }
    public enum ActionKind { Invoke, SetValue, Toggle, DoubleClick, RightClick, DragDrop, Hover, PressKeys, Scroll, SikuliClick, SikuliType }

    /// <summary>
    /// One entry in the authored sequence — either a recorded action or a
    /// manually-inserted verification. Mirrors the shape of
    /// recorder/recorded_sequence.json in the Python framework, so the
    /// same generator logic (ScriptGenerator) produces output consistent
    /// with recorder/converter.py.
    /// </summary>
    public class RecordedStep
    {
        public StepKind Kind { get; set; }
        public string Alias { get; set; } = "";
        public ActionKind Action { get; set; }

        /// <summary>SetValue's value, or a Verify step's expected text. Null for Invoke/Toggle.</summary>
        public string? Value { get; set; }

        /// <summary>Attribute name for VerifyAttribute/CheckpointAttribute steps.</summary>
        public string? AttributeName { get; set; }

        /// <summary>Target alias for DragDrop steps.</summary>
        public string? TargetAlias { get; set; }

        /// <summary>Property name for Property Checkpoint steps.</summary>
        public string? PropertyName { get; set; }

        /// <summary>Expected count for Count Checkpoint steps.</summary>
        public string? ExpectedCount { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>True if this step targets a control with no reliable
        /// AutomationId (resolved via WPFSpy at record time) — surfaced in
        /// the UI as a small badge, same signal as the real framework's
        /// self-healing locator demo.</summary>
        public bool NonStandard { get; set; }

        /// <summary>App context ID for multi-app recording. Null for single-app recordings.</summary>
        public string? AppId { get; set; }

        public string DisplayVerb => Kind switch
        {
            StepKind.Verify => "Verify Element Text",
            StepKind.VerifyOcr => "Get Data Grid Content Ocr",
            StepKind.VerifyEnabled => "Verify Element Enabled",
            StepKind.VerifyVisible => "Verify Element Visible",
            StepKind.VerifyContains => "Verify Element Contains Text",
            StepKind.VerifyRegex => "Verify Element Text Matches Regex",
            StepKind.VerifyAttribute => "Verify Element Attribute",
            StepKind.WaitExists => "Wait Until Element Exists",
            StepKind.WaitVisible => "Wait Until Element Visible",
            StepKind.WaitEnabled => "Wait Until Element Enabled",
            StepKind.WaitTextContains => "Wait Until Text Contains",
            StepKind.CheckpointProperty => "Property Checkpoint",
            StepKind.CheckpointArea => "Area Checkpoint (OCR)",
            StepKind.CheckpointImage => "Image Checkpoint",
            StepKind.CheckpointDataGrid => "DataGrid Checkpoint",
            StepKind.CheckpointCount => "Count Checkpoint",
            StepKind.CheckpointAttribute => "Attribute Checkpoint",
            _ => Action switch
            {
                ActionKind.Invoke => "Click Element",
                ActionKind.SetValue => "Set Element Value",
                ActionKind.Toggle => "Toggle Element",
                ActionKind.DoubleClick => "Double Click Element",
                ActionKind.RightClick => "Right Click Element",
                ActionKind.DragDrop => "Drag And Drop",
                ActionKind.Hover => "Hover Over Element",
                ActionKind.PressKeys => "Press Keys",
                ActionKind.Scroll => "Scroll",
                ActionKind.SikuliClick => "Sikuli Click (Image)",
                ActionKind.SikuliType => "Sikuli Type (Image)",
                _ => "Unknown",
            },
        };
    }
}
