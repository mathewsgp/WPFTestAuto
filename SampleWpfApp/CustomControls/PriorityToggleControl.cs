using System.Windows;
using System.Windows.Media;

namespace SampleWpfApp.CustomControls
{
    /// <summary>
    /// A deliberately custom-rendered, owner-drawn toggle control — it
    /// does NOT derive from ToggleButton/CheckBox and does NOT override
    /// OnCreateAutomationPeer, so standard UI Automation (and therefore
    /// FlaUI) cannot reliably discover its checked state or invoke it.
    /// This simulates a real-world custom WPF control (e.g. a
    /// third-party component or an in-house owner-drawn control) that
    /// isn't properly exposed via UIA.
    ///
    /// It implements <see cref="ISpyInteractable"/> instead, which is all
    /// that's needed for the in-process Spy Agent to drive it directly —
    /// no AutomationPeer required. See
    /// repository/elements/orders_page.yaml's OrdersPage.PriorityCheckbox
    /// entry and docs/SELF_HEALING_LOCATORS.md for how this drives the
    /// FlaUI -> WPFSpy runtime fallback demo.
    /// </summary>
    public class PriorityToggleControl : FrameworkElement, ISpyInteractable
    {
        public bool IsToggled { get; private set; } = false;

        public PriorityToggleControl()
        {
            Width = 24;
            Height = 24;
            Focusable = true;
            MouseLeftButtonDown += (_, e) =>
            {
                Toggle();
                e.Handled = true;
            };
        }

        private void Toggle()
        {
            IsToggled = !IsToggled;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(0, 0, Width, Height);
            var fill = IsToggled ? Brushes.SeaGreen : Brushes.Gainsboro;
            dc.DrawRectangle(fill, new Pen(Brushes.DimGray, 1.5), rect);
            if (IsToggled)
            {
                // Simple checkmark
                dc.DrawLine(new Pen(Brushes.White, 2), new Point(5, 12), new Point(10, 18));
                dc.DrawLine(new Pen(Brushes.White, 2), new Point(10, 18), new Point(19, 5));
            }
        }

        // --- ISpyInteractable: how the Spy Agent drives this control ---
        void ISpyInteractable.SpyInvoke() => Toggle();
        void ISpyInteractable.SpySetValue(string value) =>
            IsToggled = value.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        string ISpyInteractable.SpyGetText() => IsToggled ? "On" : "Off";
    }
}
