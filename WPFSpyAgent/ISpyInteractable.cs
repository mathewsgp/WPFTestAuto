namespace WpfSpyAgent
{
    /// <summary>
    /// Opt-in contract for custom-rendered WPF controls that don't expose
    /// a proper UI Automation peer (e.g. owner-drawn controls using
    /// OnRender) and therefore can't be reliably driven by FlaUI. A
    /// control implementing this interface is directly invokable by the
    /// Spy Agent without any dependency on UI Automation at all — this is
    /// WPFSpy's core value proposition versus FlaUI: it manipulates the
    /// visual tree and the control's own API directly, in-process,
    /// regardless of whether that control plays nicely with UIA.
    ///
    /// See SampleWpfApp/CustomControls/PriorityToggleControl.cs for a
    /// worked example.
    /// </summary>
    public interface ISpyInteractable
    {
        /// <summary>Primary action — e.g. toggles a checkbox-like control,
        /// or fires a button-like control's action.</summary>
        void SpyInvoke();

        /// <summary>Sets the control's value, if it holds one.</summary>
        void SpySetValue(string value);

        /// <summary>Returns the control's current text/value.</summary>
        string SpyGetText();
    }
}
