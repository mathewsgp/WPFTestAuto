namespace SampleWpfApp.CustomControls
{
    /// <summary>
    /// Opt-in contract for custom-rendered WPF controls that don't expose
    /// a proper UI Automation peer. A control implementing this interface
    /// can be driven directly by the Spy Agent without any dependency on
    /// UI Automation.
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
