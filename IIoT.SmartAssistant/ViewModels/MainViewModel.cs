using System.Collections.ObjectModel;
using Prism.Mvvm;
using Prism.Commands;
using IIoT.SmartAssistant.Models;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;

namespace IIoT.SmartAssistant.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private HubConnection _hubConnection;

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

        public MainViewModel() 
        {
            SendMessageCommand = new DelegateCommand(ExecuteSendMessage, () => !IsBusy);
            MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "欢迎使用工业物联网.智能助手。正在连接服务器..." });

            InitializeSignalR();
        }

        private async void InitializeSignalR()
        {
            // 根据你的 launchSettings.json，http 协议运行在 5109 端口
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5109/chathub")
                .WithAutomaticReconnect() // 断线自动重连
                .Build();

            // 监听服务端推送过来的富媒体消息指令
            _hubConnection.On<ChatMessageItem>("ReceiveMediaMessage", (mediaMsg) =>
            {
                Application.Current.Dispatcher.Invoke(() => MessageList.Add(mediaMsg));
            });

            try
            {
                await _hubConnection.StartAsync();
                Application.Current.Dispatcher.Invoke(() => MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "服务器连接成功，请发送指令。" }));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = $"连接服务器失败: {ex.Message}" }));
            }
        }

        private async void ExecuteSendMessage()
        {
            if (string.IsNullOrWhiteSpace(UserInput) || _hubConnection.State != HubConnectionState.Connected) return;

            string question = UserInput;
            UserInput = string.Empty;

            MessageList.Add(new ChatMessageItem { Role = "User", MessageType = "Text", Content = question });
            IsBusy = true;

            try
            {
                var aiMessage = new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "" };
                MessageList.Add(aiMessage);

                // 通过 SignalR 接收流式数据
                var stream = _hubConnection.StreamAsync<string>("SendMessageStream", question);

                await foreach (var chunk in stream)
                {
                    aiMessage.Content += chunk;
                }
            }
            catch (Exception ex)
            {
                MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = $"通信错误: {ex.Message}" });
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}