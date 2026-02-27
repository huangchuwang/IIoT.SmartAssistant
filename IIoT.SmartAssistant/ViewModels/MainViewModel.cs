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
                //先在界面上创建一个空的 AI 气泡
                var aiMessage = new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "" };
                MessageList.Add(aiMessage);

                //实时流式更新这个气泡的内容
                await foreach (var chunk in _aiService.SendMessageStreamAsync(question))
                {
                    aiMessage.Content += chunk;
                    var index = MessageList.IndexOf(aiMessage);
                    MessageList[index] = aiMessage;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}