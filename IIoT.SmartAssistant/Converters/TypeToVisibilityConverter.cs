using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IIoT.SmartAssistant.Converters
{
    public class TypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value 是当前消息的 MessageType (如 "Image", "Video")
            // parameter 是我们在 XAML 里传入的对比值
            string messageType = value as string;
            string targetTypeStr = parameter as string;

            if (messageType != null && messageType.Equals(targetTypeStr, StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}