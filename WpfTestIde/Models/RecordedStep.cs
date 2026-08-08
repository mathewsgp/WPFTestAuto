using System;

namespace WpfTestIde.Models
{
    public enum StepKind { Action, Verify, VerifyOcr }
    public enum ActionKind { Invoke, SetValue, Toggle }

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
            _ => Action switch
            {
                ActionKind.Invoke => "Click Element",
                ActionKind.SetValue => "Set Element Value",
                ActionKind.Toggle => "Toggle Element",
                _ => "Unknown",
            },
        };
    }
}
