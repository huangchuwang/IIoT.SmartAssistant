using System.Threading.Tasks;
using System.Windows;
using Prism.Mvvm;
using Prism.Commands;
using IIoT.SmartAssistant.Services;

namespace IIoT.SmartAssistant.ViewModels
{
    // 1. 继承 Prism 的 BindableBase
    public class MainViewModel : BindableBase
    {
        private readonly AIChatService _aiService;

        // 2. 手写通知属性
        private string _userInput = string.Empty;
        public string UserInput
        {
            get => _userInput;
            set => SetProperty(ref _userInput, value);
        }

        private string _chatLog = "AI: 欢迎使用工业物联网.智能助手。请输入指令...\n";
        public string ChatLog
        {
            get => _chatLog;
            set => SetProperty(ref _chatLog, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                // 当 IsBusy 改变时，通知按钮重新计算是否可用
                if (SetProperty(ref _isBusy, value))
                {
                    SendMessageCommand.RaiseCanExecuteChanged();
                }
            }
        }

        // 3. 定义 Prism 的 DelegateCommand
        public DelegateCommand SendMessageCommand { get; }

        // 4. 依赖注入：Prism 在创建这个 ViewModel 时，会自动把 App.xaml.cs 里注册的 AIChatService 传进来
        public MainViewModel(AIChatService aiService)
        {
            _aiService = aiService;

            // 初始化命令，并绑定执行逻辑与能否执行的条件
            SendMessageCommand = new DelegateCommand(ExecuteSendMessage, CanExecuteSendMessage);
        }

        private bool CanExecuteSendMessage()
        {
            // 如果系统正在忙，按钮禁用
            return !IsBusy;
        }

        // 因为 DelegateCommand 原生支持 async void，这里为了简单直接使用 async void 包装 Task
        private async void ExecuteSendMessage()
        {
            await ExecuteSendMessageAsync();
        }

        private async Task ExecuteSendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            string question = UserInput;
            UserInput = string.Empty;
            ChatLog += $"\n我: {question}\nAI: ";
            IsBusy = true;

            try
            {
                await foreach (var chunk in _aiService.SendMessageStreamAsync(question))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatLog += chunk;
                    });
                }
                ChatLog += "\n";
            }
            catch (System.Exception ex)
            {
                ChatLog += $"\n[系统异常]: {ex.Message}\n";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}