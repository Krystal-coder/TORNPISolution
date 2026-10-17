using System.Text.Json;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 测试方案保存/加载服务：将方案配置以 JSON 文件形式存储在程序目录下的 Plans 文件夹
    /// </summary>
    public class TestPlanService : ITestPlanService
    {
        private readonly string _plansDirectory;
        private readonly ILogService _log = new LogService(nameof(TestPlanService));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public TestPlanService()
        {
            // 方案文件保存在程序运行目录下的 Plans 文件夹
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _plansDirectory = Path.Combine(baseDir, "Plans");
            Directory.CreateDirectory(_plansDirectory);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetPlanNames()
        {
            _log.Info($"{DateTime.Now} Start calling the TestPlanService.GetPlanNames() method . {Environment.NewLine}");
            try
            {
                if (!Directory.Exists(_plansDirectory))
                {
                    return Array.Empty<string>();
                }

                return Directory.GetFiles(_plansDirectory, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .OrderBy(name => name)
                    .ToList()!;
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestPlanService.class GetPlanNames method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestPlanService.GetPlanNames() method . {Environment.NewLine}");
            }
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, TestPlanConfig> GetAllPlans()
        {
            var result = new Dictionary<string, TestPlanConfig>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(_plansDirectory))
                {
                    return result;
                }

                // 这里不逐个文件进出日志（列表过滤会反复用到），坏文件只记一行警告
                foreach (var file in Directory.GetFiles(_plansDirectory, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name)) continue;

                    try
                    {
                        var config = JsonSerializer.Deserialize<TestPlanConfig>(File.ReadAllText(file), JsonOptions);
                        if (config != null) result[name] = config;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"方案文件解析失败，列表已跳过：{file}（{ex.Message}）");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestPlanService.class GetAllPlans method, message: {ex}", ex);
            }
            return result;
        }

        /// <inheritdoc/>
        public TestPlanConfig? LoadPlan(string planName)
        {
            _log.Info($"{DateTime.Now} Start calling the TestPlanService.LoadPlan() method . {Environment.NewLine}");
            try
            {
                var filePath = GetPlanFilePath(planName);
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<TestPlanConfig>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestPlanService.class LoadPlan method, message: {ex}", ex);
                return null;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestPlanService.LoadPlan() method . {Environment.NewLine}");
            }
        }

        /// <inheritdoc/>
        public void SavePlan(TestPlanConfig config)
        {
            _log.Info($"{DateTime.Now} Start calling the TestPlanService.SavePlan() method . {Environment.NewLine}");
            try
            {
                if (string.IsNullOrWhiteSpace(config.PlanName))
                {
                    throw new ArgumentException("方案名称不能为空", nameof(config));
                }

                var filePath = GetPlanFilePath(config.PlanName);
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestPlanService.class SavePlan method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestPlanService.SavePlan() method . {Environment.NewLine}");
            }
        }

        /// <inheritdoc/>
        public void DeletePlan(string planName)
        {
            _log.Info($"{DateTime.Now} Start calling the TestPlanService.DeletePlan() method . {Environment.NewLine}");
            try
            {
                var filePath = GetPlanFilePath(planName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"An error occured in TestPlanService.class DeletePlan method, message: {ex}", ex);
                throw;
            }
            finally
            {
                _log.Info($"{DateTime.Now} Ended calling the TestPlanService.DeletePlan() method . {Environment.NewLine}");
            }
        }

        /// <summary>根据方案名称获取 JSON 文件完整路径</summary>
        private string GetPlanFilePath(string planName)
        {
            // 过滤文件名中的非法字符
            var safeName = string.Join("_", planName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_plansDirectory, $"{safeName}.json");
        }
    }
}
