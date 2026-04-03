using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PlayniteOverlay.Services;

namespace PlayniteOverlay;

public partial class ControllerDiagnosticView : UserControl
{
    private readonly ControllerDiagnosticService diagnosticService;

    public ControllerDiagnosticView(ControllerDiagnosticService diagnosticService)
    {
        this.diagnosticService = diagnosticService ?? throw new ArgumentNullException(nameof(diagnosticService));
        InitializeComponent();
        DevicesList.ItemsSource = diagnosticService.Devices;

        diagnosticService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ControllerDiagnosticService.Devices))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DevicesList.ItemsSource = diagnosticService.Devices;
                }));
            }
            else if (e.PropertyName == nameof(ControllerDiagnosticService.StatusText))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = diagnosticService.StatusText;
                }));
            }
        };
    }

    private void ToggleDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (diagnosticService.IsPolling)
        {
            diagnosticService.StopPolling();
            ToggleBtn.Content = "Start Diagnostic";
        }
        else
        {
            diagnosticService.StartPolling();
            ToggleBtn.Content = "Stop Diagnostic";
        }
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (oldParent != null && VisualParent == null)
        {
            diagnosticService.StopPolling();
        }
    }
}

internal class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

internal class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is false ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

internal class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.FromArgb(200, 76, 175, 80))
            : new SolidColorBrush(Color.FromArgb(200, 200, 60, 60));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

internal class ButtonStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.FromArgb(220, 76, 175, 80))
            : new SolidColorBrush(Color.FromArgb(220, 60, 60, 60));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

internal class ButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Colors.White)
            : new SolidColorBrush(Color.FromArgb(200, 180, 180, 180));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
