using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using Prism.Commands;
using Prism.Mvvm;
using IIoT.SmartAssistant.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace IIoT.SmartAssistant.ViewModels
{
    public class ChartConfigDto
    {
        public string action { get; set; }
        public string chartType { get; set; }
        public string title { get; set; }
        public string[] xAxis { get; set; }
        public double[] series { get; set; }
    }

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
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5109/chathub")
                .WithAutomaticReconnect()
                .Build();

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

                var stream = _hubConnection.StreamAsync<string>("SendMessageStream", question);

                string fullResponse = "";
                bool isLikelyJson = false;

                await foreach (var chunk in stream)
                {
                    fullResponse += chunk;

                    if (!isLikelyJson && (fullResponse.TrimStart().StartsWith("{") || fullResponse.TrimStart().StartsWith("```json")))
                    {
                        isLikelyJson = true;
                        Application.Current.Dispatcher.Invoke(() => {
                            aiMessage.Content = "正在为您渲染数据图表，请稍候...";
                        });
                    }

                    if (!isLikelyJson)
                    {
                        Application.Current.Dispatcher.Invoke(() => {
                            aiMessage.Content = fullResponse;
                        });
                    }
                }

                string finalContent = fullResponse.Trim();

                int startIndex = finalContent.IndexOf('{');
                int endIndex = finalContent.LastIndexOf('}');

                if (startIndex != -1 && endIndex > startIndex && (finalContent.Contains("\"render_chart\"") || finalContent.Contains("\"render_Chart\"")))
                {
                    try
                    {
                        string jsonStr = finalContent.Substring(startIndex, endIndex - startIndex + 1);

                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
                        };

                        var chartData = JsonSerializer.Deserialize<ChartConfigDto>(jsonStr, options);

                        if (chartData != null && chartData.action?.ToLower() == "render_chart")
                        {
                            if (chartData.xAxis == null || chartData.series == null || chartData.series.Length == 0)
                                throw new Exception("解析失败：大模型返回的图表坐标轴或数据列为空。");

                            var safeTitle = string.IsNullOrWhiteSpace(chartData.title) ? "数据统计图" : chartData.title;

                            // ==============================================================
                            // 战术第一步：先触发 UI 显示（此时图表是空的白板，但是 WPF 开始计算宽高了）
                            // ==============================================================
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                aiMessage.ChartTitle = safeTitle;
                                aiMessage.MessageType = "Chart";
                            });

                            // ==============================================================
                            // 战术第二步：让子弹飞一会儿，极其关键！！！给 WPF 100毫秒去撑开容器
                            // ==============================================================
                            await Task.Delay(100);

                            // ==============================================================
                            // 战术第三步：在容器真正有了尺寸之后，再把数据塞进去强制唤醒重绘！
                            // ==============================================================
                            // ==============================================================
                            // 战术第三步：在容器真正有了尺寸之后，再把数据塞进去强制唤醒重绘！
                            // ==============================================================
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                // 【关键修复】：保留原有的 ObservableCollection 实例对象，通过 Clear 和 Add 触发增量更新

                                // 1. 更新 X 轴
                                aiMessage.ChartXAxes.Clear();
                                aiMessage.ChartXAxes.Add(new Axis { Labels = chartData.xAxis.ToList() });

                                // 2. 更新数据列 Series
                                aiMessage.ChartSeries.Clear();
                                if (chartData.chartType?.ToLower() == "bar" || chartData.chartType?.ToLower() == "column")
                                {
                                    aiMessage.ChartSeries.Add(new ColumnSeries<double>
                                    {
                                        Values = chartData.series.ToList(),
                                        Name = safeTitle
                                    });
                                }
                                else
                                {
                                    aiMessage.ChartSeries.Add(new LineSeries<double>
                                    {
                                        Values = chartData.series.ToList(),
                                        Name = safeTitle,
                                        LineSmoothness = 0.5
                                    });
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() => {
                            aiMessage.Content = $"[图表渲染异常] {ex.Message} \n\n【排错参考-大模型返回的原始数据】:\n{finalContent}";
                            aiMessage.MessageType = "Text";
                        });
                    }
                }
                else if (isLikelyJson)
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        aiMessage.Content = finalContent;
                        aiMessage.MessageType = "Text";
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => {
                    MessageList.Add(new ChatMessageItem { Role = "AI", MessageType = "Text", Content = $"通信错误: {ex.Message}" });
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() => {
                    IsBusy = false;
                });
            }
        }
    }
}