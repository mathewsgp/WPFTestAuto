using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace SampleWpfApp.CustomControls
{
    /// <summary>
    /// A custom-rendered toggle control that IS exposed via UI Automation
    /// through a custom AutomationPeer, so both FlaUI and WPFSpy can
    /// discover and drive it.
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
                dc.DrawLine(new Pen(Brushes.White, 2), new Point(5, 12), new Point(10, 18));
                dc.DrawLine(new Pen(Brushes.White, 2), new Point(10, 18), new Point(19, 5));
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new PriorityToggleAutomationPeer(this);
        }

        // --- ISpyInteractable: how the Spy Agent drives this control ---
        void ISpyInteractable.SpyInvoke() => Toggle();
        void ISpyInteractable.SpySetValue(string value) =>
            IsToggled = value.Equals("On", StringComparison.OrdinalIgnoreCase);
        string ISpyInteractable.SpyGetText() => IsToggled ? "On" : "Off";
    }

    public class PriorityToggleAutomationPeer : FrameworkElementAutomationPeer
    {
        public PriorityToggleAutomationPeer(FrameworkElement owner) : base(owner) { }

        protected override string GetNameCore()
        {
            return ((PriorityToggleControl)Owner).Name ?? "PriorityToggle";
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.CheckBox;
        }

        protected override bool IsControlElementCore()
        {
            return true;
        }

        protected override bool IsContentElementCore()
        {
            return true;
        }
    }
}
