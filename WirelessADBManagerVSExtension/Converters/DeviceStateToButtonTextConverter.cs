using System;
using System.Globalization;
using System.Windows.Data;
using WirelessADBManagerVSExtension.Models;

namespace WirelessADBManagerVSExtension.Converters;

[ValueConversion(typeof(DeviceStates), typeof(string))]
public class DeviceStateToButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var state = (DeviceStates)value;

        switch (state)
        {
            case DeviceStates.ManualPair:
                return "Pair";
            case DeviceStates.Pairing:
                return "Pairing";
            case DeviceStates.Connecting:
                return "Connecting";
            case DeviceStates.Connected:
                return "Disconnect";
            case DeviceStates.Disconnected:
                return "Connect";
            default:
                return "Unknown";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
