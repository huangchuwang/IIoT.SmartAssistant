using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;
using System.Text.Json;
using IIoT.SmartAssistant.Server.Models;
using Microsoft.AspNetCore.SignalR;
using IIoT.SmartAssistant.Server.Hubs;
using Microsoft.Extensions.Configuration;

namespace IIoT.SmartAssistant.Server.Plugins
{
    public class DynamicDatabasePlugin
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly string _connectionString;

        public DynamicDatabasePlugin(IHubContext<ChatHub> hubContext, IConfiguration configuration)
        {
            _hubContext = hubContext;
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                                ?? throw new InvalidOperationException("Database connection string 'DefaultConnection' not found.");
        }

        [KernelFunction, Description("执行 SQL SELECT 查询语句，获取工业物联网数据库中的实时或历史统计数据。")]
        public async Task<string> ExecuteSqlQueryAsync(
            [Description("大模型根据用户需求生成的、合法的 SQL SELECT 语句")] string sqlQuery)
        {
            if (!sqlQuery.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return "仅支持 SELECT 查询语句。";
            }

            try
            {
                //使用 SignalR 向客户端推送数据检索提示
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
            catch (SqlException ex)
            {
                return $"执行 SQL 失败，数据库错误: {ex.Message}。请检查你的 SQL 语法并重新调用本工具。";
            }
            catch (Exception ex)
            {
                return $"执行 SQL 失败，发生未知错误: {ex.Message}。";
            }
        }
    }
}