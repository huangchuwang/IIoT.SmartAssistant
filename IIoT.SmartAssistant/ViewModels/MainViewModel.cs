using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.SmartAssistant.Services;
using System.Windows;

namespace IIoT.SmartAssistant.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly AIChatService _aiService;

        [ObservableProperty]
        private string _userInput = string.Empty;

        [ObservableProperty]
        private string _chatLog = "AI: 欢迎使用智能运维系统。请输入指令...\n";

        [ObservableProperty]
        private bool _isBusy;

        public MainViewModel()
        {
            _aiService = new AIChatService();
        }

        [RelayCommand]
        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            string question = UserInput;
            UserInput = string.Empty; // 清空输入框
            ChatLog += $"\n我: {question}\nAI: ";
            IsBusy = true;

            try
            {
                // 接收流式返回的数据并实时更新 UI
                await foreach (var chunk in _aiService.SendMessageStreamAsync(question))
                {
                    // 确保在 UI 线程更新
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