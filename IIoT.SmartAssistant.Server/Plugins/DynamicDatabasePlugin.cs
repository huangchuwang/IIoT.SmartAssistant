using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;
using System.Text.Json;
using IIoT.SmartAssistant.Server.Models;
using Microsoft.AspNetCore.SignalR;
using IIoT.SmartAssistant.Server.Hubs;
using Microsoft.Extensions.Configuration; // 新增注入配置的命名空间

namespace IIoT.SmartAssistant.Server.Plugins
{
    public class DynamicDatabasePlugin
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly string _connectionString;

        // 【核心修复】增加 IConfiguration 参数，接收 AIChatService 传来的配置
        public DynamicDatabasePlugin(IHubContext<ChatHub> hubContext, IConfiguration configuration)
        {
            _hubContext = hubContext;
            // 从 appsettings.json 中读取 "DefaultConnection"
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? "Server=localhost;Database=IIoT_DB;User Id=sa;Password=123456;TrustServerCertificate=True;";
        }

        [KernelFunction, Description("执行 SQL SELECT 查询语句，获取工业物联网数据库中的实时或历史统计数据。")]
        public async Task<string> ExecuteSqlQueryAsync(
            [Description("大模型根据用户需求生成的、合法的 SQL SELECT 语句")] string sqlQuery)
        {
            try
            {
                // 使用 SignalR 向客户端推送数据检索提示
                await _hubContext.Clients.All.SendAsync("ReceiveMediaMessage", new ChatMessageItem
                {
                    Role = "AI",
                    MessageType = "Text",
                    Content = $"正在执行底层数据检索: \n{sqlQuery}"
                });

                using SqlConnection conn = new SqlConnection(_connectionString);
                using SqlCommand cmd = new SqlCommand(sqlQuery, conn);
                using SqlDataAdapter adapter = new SqlDataAdapter(cmd);

                DataTable dt = new DataTable();
                await Task.Run(() => adapter.Fill(dt));

                if (dt.Rows.Count == 0)
                {
                    return "查询成功，但数据库中没有符合条件的数据。";
                }

                var results = new List<Dictionary<string, object>>();
                foreach (DataRow row in dt.Rows)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (DataColumn col in dt.Columns)
                    {
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                    }
                    results.Add(dict);
                }

                string jsonData = JsonSerializer.Serialize(results);
                return $"查询结果(JSON格式): {jsonData}。";
            }
            catch (Exception ex)
            {
                return $"执行 SQL 失败，错误信息: {ex.Message}。请检查你的 SQL 语法并重新调用本工具。";
            }
        }
    }
}