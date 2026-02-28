using System.Collections.ObjectModel;
using Prism.Mvvm;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace IIoT.SmartAssistant.Models
{
    public class ChatMessageItem : BindableBase
    {
        private string _role;
        public string Role
        {
            get => _role;
            set => SetProperty(ref _role, value);
        }

        private string _content;
        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        private string _messageType;
        public string MessageType
        {
            get => _messageType;
            set => SetProperty(ref _messageType, value);
        }

        private string _mediaPath;
        public string MediaPath
        {
            get => _mediaPath;
            set => SetProperty(ref _mediaPath, value);
        }

        private string _chartTitle;
        public string ChartTitle
        {
            get => _chartTitle;
            set => SetProperty(ref _chartTitle, value);
        }

        // 【核心修复】：直接在这里 new 出实例，保证永远不为 null
        private ObservableCollection<ISeries> _chartSeries = new ObservableCollection<ISeries>();
        public ObservableCollection<ISeries> ChartSeries
        {
            get => _chartSeries;
            set => SetProperty(ref _chartSeries, value);
        }

        // 【核心修复】：直接在这里 new 出实例，保证永远不为 null
        private ObservableCollection<Axis> _chartXAxes = new ObservableCollection<Axis>();
        public ObservableCollection<Axis> ChartXAxes
        {
            get => _chartXAxes;
            set => SetProperty(ref _chartXAxes, value);
        }
    }
}