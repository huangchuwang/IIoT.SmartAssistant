using System.Collections.ObjectModel;
using Prism.Mvvm;
using Prism.Commands;
using Prism.Events;
using IIoT.SmartAssistant.Services;
using IIoT.SmartAssistant.Models;
using System.Windows;

namespace IIoT.SmartAssistant.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private readonly AIChatService _aiService;
        private readonly IEventAggregator _eventAggregator;

        // 核心变更：用集合代替纯文本字符串
        public ObservableCollection<ChatMessageItem> MessageList { get; set; } = new ObservableCollection<ChatMessageItem>();

        private string _userInput = string.Empty;
        public string UserInput
        {
            get => _userInput;
            set => SetProperty(ref _userInput, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (SetProperty(ref _isBusy, value)) SendMessageCommand.RaiseCanExecuteChanged(); }
        }

        public DelegateCommand SendMessageCommand { get; }

        public MainViewModel(AIChatService aiService, IEventAggregator eventAggregator)
        {
            _aiService = aiService;
            _eventAggregator = eventAggregator;
            SendMessageCommand = new DelegateCommand(ExecuteSendMessage, () => !IsBusy);

            // 订阅后台 AI 插件发出的“富媒体展示”指令
            _eventAggregator.GetEvent<MediaMessageEvent>().Subscribe(OnMediaMessageReceived);

            MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "欢迎使用工业物联网.智能助手。请输入指令..." });
        }

        private void OnMediaMessageReceived(ChatMessageItem mediaMsg)
        {
            // 确保在 UI 线程添加
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageList.Add(mediaMsg);
            });
        }

        private async void ExecuteSendMessage()
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            string question = UserInput;
            UserInput = string.Empty;

            // 添加用户问题
            MessageList.Add(new ChatMessageItem { Role = "User", MessageType = "Text", Content = question });
            IsBusy = true;

            try
            {
                string fullResponse = "";
                await foreach (var chunk in _aiService.SendMessageStreamAsync(question))
                {
                    fullResponse += chunk;
                }

                // 添加 AI 的纯文本回复 (这里的流式更新可以后续通过拆分更新最后的元素来实现，这里简化为最后一次性添加)
                MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = fullResponse });
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}