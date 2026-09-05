using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfTestIde.Models;
using WpfTestIde.Themes;

namespace WpfTestIde.Converters
{
    public class StepKindToBrushConverter : IValueConverter
    {
        private static readonly Brush LightActionBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
        private static readonly Brush LightVerifyBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xF6, 0xEC));
        private static readonly Brush LightVerifyOcrBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD));

        private static readonly Brush DarkActionBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
        private static readonly Brush DarkVerifyBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x2F));
        private static readonly Brush DarkVerifyOcrBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x2D, 0x3D));

        private static bool IsDark => Themes.ThemeManager.CurrentTheme == "Dark";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (IsDark)
            {
                return value is StepKind.VerifyOcr ? DarkVerifyOcrBrush :
                       value is StepKind.Verify ? DarkVerifyBrush : DarkActionBrush;
            }
            return value is StepKind.VerifyOcr ? LightVerifyOcrBrush :
                   value is StepKind.Verify ? LightVerifyBrush : LightActionBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class BoolToStatusBrushConverter : IValueConverter
    {
        private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xF5, 0xEA));
        private static readonly Brush FailureBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE7));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? SuccessBrush : FailureBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class EmptyStringToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class EmptyStringToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class EmptyToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrEmpty(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class ZeroToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is int intValue && intValue == 0) || (value is null) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Maps a <see cref="ToastKind"/> to a theme-correct <see cref="Brush"/>
    /// for the E3 <c>ToastBar</c> background. The toast's inner TextBlock uses
    /// <c>StatusBarTextBrush</c> (white) for its Foreground, so the Background
    /// must stay dark enough in BOTH themes to keep white text readable. For
    /// <see cref="ToastKind.Success"/>/<see cref="ToastKind.Warning"/>/
    /// <see cref="ToastKind.Error"/> the existing semantic brushes
    /// (<c>SuccessBrush</c>/<c>WarningBrush</c>/<c>ErrorBrush</c>) are vivid
    /// enough to read white against in both themes, so the converter resolves
    /// them via <see cref="Application.Current"/>.<see cref="ResourceDictionary.Contains"/> /
    /// indexer so theme swaps (managed by <c>ThemeManager</c>) are picked up.
    /// For <see cref="ToastKind.Info"/> however <c>TextPrimaryBrush</c> — the
    /// obvious key to reuse — flips to a light <c>#CCCCCC</c> in the dark
    /// theme, giving white-on-near-white unreadable toast text. Info instead
    /// resolves a hardcoded dark-slate <see cref="SolidColorBrush"/> whose
    /// value flips with <see cref="ThemeManager.CurrentTheme"/>: it stays a
    /// medium-dark accent in either theme so white text always reads. Kept
    /// dependency-free (matches the rest of <c>Converters.cs</c>).
    /// </summary>
    public class ToastKindToBrushConverter : IValueConverter
    {
        // Info toast background: picked so white StatusBarTextBrush foreground
        // reads against it in BOTH themes (#2D2D30 is PanelBrush-dark in dark
        // theme; #44475A is a darker slate that reads white in light theme).
        // Not a themable resource on purpose — see the class XML doc.
        private static readonly Brush InfoBrushLightTheme = new SolidColorBrush(Color.FromRgb(0x44, 0x47, 0x5A));
        private static readonly Brush InfoBrushDarkTheme = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not ToastKind kind) return DependencyProperty.UnsetValue;
            if (kind == ToastKind.Info)
            {
                return ThemeManager.CurrentTheme == "Dark"
                    ? InfoBrushDarkTheme
                    : InfoBrushLightTheme;
            }
            var app = Application.Current;
            if (app != null && app.Resources.Contains(ResourceKeyFor(kind)))
            {
                return app.Resources[ResourceKeyFor(kind)];
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static string ResourceKeyFor(ToastKind kind) => kind switch
        {
            ToastKind.Success => "SuccessBrush",
            ToastKind.Warning => "WarningBrush",
            ToastKind.Error => "ErrorBrush",
            _ => "TextPrimaryBrush",
        };
    }
}
