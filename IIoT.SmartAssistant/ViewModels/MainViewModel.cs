using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using Prism.Commands;
using Prism.Mvvm;
using IIoT.SmartAssistant.Models;

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

        public ObservableCollection<string> SearchModes { get; set; } = new ObservableCollection<string>
        {
            "全局智能 (Auto)",
            "知识问答 (Docs)",
            "数据报表 (DB)",
            "设备控制 (IoT)"
        };

        private string _selectedSearchMode = "全局智能 (Auto)";
        public string SelectedSearchMode
        {
            get => _selectedSearchMode;
            set => SetProperty(ref _selectedSearchMode, value);
        }

        private ChatMessageItem _currentStreamingMessage;

        public DelegateCommand SendMessageCommand { get; }

        public DelegateCommand<string> OpenFileCommand { get; }

        public MainViewModel()
        {
            SendMessageCommand = new DelegateCommand(ExecuteSendMessage, () => !IsBusy);

            OpenFileCommand = new DelegateCommand<string>(url =>
            {
                if (!string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            });

            MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "欢迎使用工业物联网.智能助手。正在连接服务器..." });
            InitializeSignalR();
        }

        private async void InitializeSignalR()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5109/chathub")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ChatMessageItem>("ReceiveMediaMessage", (mediaMsg) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_currentStreamingMessage != null && MessageList.Contains(_currentStreamingMessage))
                    {
                        int index = MessageList.IndexOf(_currentStreamingMessage);
                        MessageList.Insert(index, mediaMsg);
                    }
                    else
                    {
                        MessageList.Add(mediaMsg);
                    }
                });
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
                _currentStreamingMessage = new ChatMessageItem { Role = "AI", MessageType = "Text", Content = "思考中..." };
                MessageList.Add(_currentStreamingMessage);

                var stream = _hubConnection.StreamAsync<string>("SendMessageStream", question, SelectedSearchMode);

                string fullResponse = "";
                bool isFirstChunk = true;

                await foreach (var chunk in stream)
                {
                    fullResponse += chunk; // 收集完整的回复文本用于校验JSON

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (isFirstChunk)
                        {
                            _currentStreamingMessage.Content = "";
                            isFirstChunk = false;
                        }
                        _currentStreamingMessage.Content += chunk;
                    });
                }

                string finalContent = fullResponse.Trim();
                int startIndex = finalContent.IndexOf('{');
                int endIndex = finalContent.LastIndexOf('}');

                if (startIndex != -1 && endIndex > startIndex && finalContent.Contains("\"send_file\""))
                {
                    try
                    {
                        string jsonStr = finalContent.Substring(startIndex, endIndex - startIndex + 1);
                        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip };
                        var fileData = System.Text.Json.JsonSerializer.Deserialize<FileConfigDto>(jsonStr, options);

                        if (fileData != null && fileData.action?.ToLower() == "send_file")
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _currentStreamingMessage.FileName = fileData.fileName;
                                _currentStreamingMessage.FileUrl = fileData.url;
                                _currentStreamingMessage.MessageType = "File"; // 触发 UI 卡片显示
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _currentStreamingMessage.Content = $"[文件渲染异常] {ex.Message}";
                        });
                    }
                }
                else if (isFirstChunk && _currentStreamingMessage.Content == "思考中...")
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _currentStreamingMessage.Content = "执行完毕。";
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = $"通信错误: {ex.Message}" });
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _currentStreamingMessage = null;
                    IsBusy = false;
                });
            }
        }
    }
}