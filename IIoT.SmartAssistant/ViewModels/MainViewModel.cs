using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
                        // 切换到主线程更新 UI
                        Application.Current.Dispatcher.Invoke(() => {
                            aiMessage.Content = "正在为您渲染数据图表，请稍候...";
                        });
                    }

                    // 如果不是 JSON，才正常流式打字输出
                    if (!isLikelyJson)
                    {
                        Application.Current.Dispatcher.Invoke(() => {
                            aiMessage.Content = fullResponse;
                        });
                    }
                }

                string finalContent = fullResponse.Trim();
                if (finalContent.Contains("\"action\"") && (finalContent.Contains("\"render_chart\"") || finalContent.Contains("\"render_Chart\"")))
                {
                    try
                    {
                        var match = Regex.Match(finalContent, @"\{.*\}", RegexOptions.Singleline);
                        if (match.Success)
                        {
                            // 【鲁棒性优化】极大地放宽 JSON 解析规则，防止大模型抽风
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true, // 忽略大小写
                                AllowTrailingCommas = true,         // 允许结尾多余的逗号
                                ReadCommentHandling = JsonCommentHandling.Skip, // 跳过大模型擅自加的注释
                                // 允许大模型把数字用引号包起来（如 "25.0"）
                                NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
                            };

                            var chartData = JsonSerializer.Deserialize<ChartConfigDto>(match.Value, options);

                            if (chartData != null && chartData.action?.ToLower() == "render_chart")
                            {
                                if (chartData.xAxis == null || chartData.series == null)
                                    throw new Exception("解析失败：大模型返回的图表坐标轴或数据列为空。");

                                // 【防闪退核心】必须在 WPF 的主 UI 线程上实例化 LiveCharts 的组件
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    aiMessage.ChartTitle = string.IsNullOrWhiteSpace(chartData.title) ? "数据统计图" : chartData.title;

                                    // 【核心修复】：使用 Clear() 和 Add()，而不是重新 new 集合，确保 WPF 绑定不丢失！
                                    aiMessage.ChartXAxes.Clear();
                                    aiMessage.ChartXAxes.Add(new Axis { Labels = chartData.xAxis.ToList() });

                                    var safeTitle = aiMessage.ChartTitle;
                                    var safeSeries = chartData.series.ToList();

                                    aiMessage.ChartSeries.Clear();

                                    // 判断并设置图表类型
                                    if (chartData.chartType?.ToLower() == "bar")
                                    {
                                        aiMessage.ChartSeries.Add(new ColumnSeries<double>
                                        {
                                            Values = safeSeries,
                                            Name = safeTitle
                                        });
                                    }
                                    else
                                    {
                                        aiMessage.ChartSeries.Add(new LineSeries<double>
                                        {
                                            Values = safeSeries,
                                            Name = safeTitle,
                                            LineSmoothness = 0.5
                                        });
                                    }

                                    // 完美！触发转换器，隐藏掩盖文本，展示炫酷图表
                                    aiMessage.MessageType = "Chart";
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 解析或渲染失败时，将底层的报错原因显示出来，方便排错
                        Application.Current.Dispatcher.Invoke(() => {
                            aiMessage.Content = $"[图表渲染异常] {ex.Message} \n\n【排错参考-大模型返回的原始数据】:\n{finalContent}";
                            aiMessage.MessageType = "Text";
                        });
                    }
                }
                else if (isLikelyJson)
                {
                    // 如果被误判为 JSON（比如大模型输出了别的代码），则恢复显示原始内容
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