using System;
using System.Globalization;
using System.Windows.Data;

namespace IIoT.SmartAssistant.Converters
{
    // 实现 IValueConverter 接口
    public class InverseBooleanConverter : IValueConverter
    {
        // 核心逻辑：把 true 变 false，false 变 true
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isBusy)
            {
                return !isBusy;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}