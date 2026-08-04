using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfTestIde.Models;

namespace WpfTestIde.Converters
{
    public class StepKindToBrushConverter : IValueConverter
    {
        private static readonly Brush ActionBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA));
        private static readonly Brush VerifyBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xF6, 0xEC));
        private static readonly Brush VerifyOcrBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is StepKind.VerifyOcr ? VerifyOcrBrush :
            value is StepKind.Verify ? VerifyBrush : ActionBrush;

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
}
