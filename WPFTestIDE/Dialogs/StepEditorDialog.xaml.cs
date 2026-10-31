using System;
using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Models;

namespace WpfTestIde.Dialogs
{
    /// <summary>
    /// Visual editor for a single RecordedStep. Allows changing kind, action,
    /// alias, value, attribute name, target alias, property name, expected count,
    /// and the non-standard flag. On OK, the ResultStep carries the edited values.
    /// </summary>
    public partial class StepEditorDialog : Window
    {
        public RecordedStep ResultStep { get; private set; }

        private readonly RecordedStep _original;

        public StepEditorDialog(RecordedStep step)
        {
            _original = step ?? throw new ArgumentNullException(nameof(step));
            ResultStep = step.Clone();
            InitializeComponent();

            // Populate fields from the cloned step
            AliasBox.Text = ResultStep.Alias;
            ValueBox.Text = ResultStep.Value ?? "";
            AttributeNameBox.Text = ResultStep.AttributeName ?? "";
            TargetAliasBox.Text = ResultStep.TargetAlias ?? "";
            PropertyNameBox.Text = ResultStep.PropertyName ?? "";
            ExpectedCountBox.Text = ResultStep.ExpectedCount ?? "";
            NonStandardCheck.IsChecked = ResultStep.NonStandard;

            // Select the current kind and action in the combos
            KindCombo.SelectedItem = ResultStep.Kind;
            ActionCombo.SelectedItem = ResultStep.Action;

            // Wire up kind/action changes to show/hide relevant fields
            KindCombo.SelectionChanged += KindCombo_SelectionChanged;
            ActionCombo.SelectionChanged += ActionCombo_SelectionChanged;

            UpdateFieldVisibility();
        }

        private void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultStep != null)
                ResultStep.Kind = (StepKind)KindCombo.SelectedItem!;
            UpdateFieldVisibility();
        }

        private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultStep != null)
                ResultStep.Action = (ActionKind)ActionCombo.SelectedItem!;
            UpdateFieldVisibility();
        }

        private void UpdateFieldVisibility()
        {
            var kind = ResultStep.Kind;
            var action = ResultStep.Action;

            // Value field: relevant for Verify/Wait/Checkpoint kinds and SetValue/PressKeys/Scroll actions
            bool showValue = kind == StepKind.Verify || kind == StepKind.VerifyContains ||
                             kind == StepKind.VerifyRegex || kind == StepKind.VerifyAttribute ||
                             kind == StepKind.WaitExists || kind == StepKind.WaitVisible ||
                             kind == StepKind.WaitEnabled || kind == StepKind.WaitTextContains ||
                             kind == StepKind.CheckpointProperty || kind == StepKind.CheckpointArea ||
                             kind == StepKind.CheckpointImage || kind == StepKind.CheckpointDataGrid ||
                             kind == StepKind.CheckpointCount || kind == StepKind.CheckpointAttribute ||
                             action == ActionKind.SetValue || action == ActionKind.PressKeys ||
                             action == ActionKind.Scroll;

            ValueBox.IsEnabled = showValue;

            // Attribute Name: VerifyAttribute / CheckpointAttribute
            AttributeNameBox.IsEnabled = kind == StepKind.VerifyAttribute || kind == StepKind.CheckpointAttribute;

            // Property Name: CheckpointProperty
            PropertyNameBox.IsEnabled = kind == StepKind.CheckpointProperty;

            // Expected Count: CheckpointCount
            ExpectedCountBox.IsEnabled = kind == StepKind.CheckpointCount;

            // Target Alias: DragDrop
            TargetAliasBox.IsEnabled = action == ActionKind.DragDrop;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultStep.Alias = AliasBox.Text;
            ResultStep.Value = ValueBox.Text;
            ResultStep.AttributeName = AttributeNameBox.Text;
            ResultStep.TargetAlias = TargetAliasBox.Text;
            ResultStep.PropertyName = PropertyNameBox.Text;
            ResultStep.ExpectedCount = ExpectedCountBox.Text;
            ResultStep.NonStandard = NonStandardCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
