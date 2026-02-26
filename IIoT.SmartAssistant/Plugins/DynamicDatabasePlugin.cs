using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;
using Prism.Events;
using IIoT.SmartAssistant.Models;
using System.Text.Json;

namespace IIoT.SmartAssistant.Plugins
{
    public class DynamicDatabasePlugin
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly string _connectionString = "Server=localhost;Database=IIoT_DB;User Id=sa;Password=123456;TrustServerCertificate=True;";

        public DynamicDatabasePlugin(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        [KernelFunction, Description("执行 SQL SELECT 查询语句，获取工业物联网数据库中的实时或历史统计数据。")]
        public async Task<string> ExecuteSqlQueryAsync(
            [Description("大模型根据用户需求生成的、合法的 SQL SELECT 语句")] string sqlQuery)
        {
            try
            {
                // 1. 通知前端 UI：AI 正在查数据库（可选）
                _eventAggregator.GetEvent<MediaMessageEvent>().Publish(new ChatMessageItem
                {
                    Role = "AI",
                    MessageType = "Text", // 这里仅作提示
                    Content = $"正在执行底层数据检索: \n{sqlQuery}"
                });

                // 2. 使用 ADO.NET 执行动态 SQL
                using SqlConnection conn = new SqlConnection(_connectionString);
                using SqlCommand cmd = new SqlCommand(sqlQuery, conn);
                using SqlDataAdapter adapter = new SqlDataAdapter(cmd);

                DataTable dt = new DataTable();
                await Task.Run(() => adapter.Fill(dt)); // 异步填充数据

                if (dt.Rows.Count == 0)
                {
                    return "查询成功，但数据库中没有符合条件的数据。";
                }

                // 3. 将 DataTable 转换为字典列表，方便序列化为 JSON
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

                // 4. 将结果转为 JSON 返回给大模型
                string jsonData = JsonSerializer.Serialize(results);
                return $"查询结果(JSON格式): {jsonData}。请根据这些数据回答用户的问题。";
            }
            catch (Exception ex)
            {
                // 如果 AI 写的 SQL 报错了，把错误信息返回给它，强大的模型会自我纠错并重试！
                return $"执行 SQL 失败，错误信息: {ex.Message}。请检查你的 SQL 语法并重新调用本工具。";
            }
        }
    }
}