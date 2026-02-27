using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IIoT.SmartAssistant.Converters
{
    public class RoleToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string role = value as string;
            if (role == "User")
            {
                //用户消息的气泡颜色（浅蓝色）
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1F5FE"));
            }

            //AI 消息的气泡颜色（浅灰色）
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}