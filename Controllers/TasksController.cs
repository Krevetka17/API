using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToDoListAPI.Data;
using ToDoListAPI.Models;
using MailKit.Net.Smtp;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MimeKit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using MailKit;
using ToDoListAPI.Services;
using System.Text.Json; 

namespace ToDoListAPI.Controllers
{
    [Route("api/tasks")]
    [ApiController]
    public class TasksController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly SmtpSettings _smtpSettings;
        private readonly ILogger<TasksController> _logger;
        private readonly WebSocketService _wsService; // Добавляем сервис WebSocket

        public TasksController(
            AppDbContext context,
            IOptions<SmtpSettings> smtpSettings,
            ILogger<TasksController> logger,
            WebSocketService wsService) // Инжектируем WebSocketService
        {
            _context = context;
            _smtpSettings = smtpSettings.Value;
            _logger = logger;
            _wsService = wsService;
        }

        // 1. Получить все задачи
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TaskItem>>> GetTasks()
        {
            return await _context.Tasks.ToListAsync();
        }

        // 2. Получить задачу по ID
        [HttpGet("{id}")]
        public async Task<ActionResult<TaskItem>> GetTask(int id)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();
            return task;
        }

        // 3. Добавить новую задачу (с отправкой уведомления на email и WebSocket)
        [HttpPost]
        public async Task<ActionResult<TaskItem>> CreateTask(TaskItem task, [FromQuery] string recipientEmail = null)
        {
            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(recipientEmail))
            {
                await SendMailAsync(task.Title, recipientEmail, "Новая задача создана");
            }

            var message = JsonSerializer.Serialize(new { Action = "Add", Task = task });
            await _wsService.Broadcast(message);

            return CreatedAtAction(nameof(GetTask), new { id = task.Id }, task);
        }

        // 4. Обновить задачу (с отправкой уведомления на email и WebSocket)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTask(int id, TaskItem task, [FromQuery] string recipientEmail = null)
        {
            if (id != task.Id) return BadRequest();

            _context.Entry(task).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(recipientEmail))
            {
                await SendMailAsync(task.Title, recipientEmail, "Задача обновлена");
            }

            var message = JsonSerializer.Serialize(new { Action = "Update", Task = task });
            await _wsService.Broadcast(message);

            return NoContent();
        }

        // 5. Удалить задачу (с отправкой уведомления через WebSocket)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(int id)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();

            var message = JsonSerializer.Serialize(new { Action = "Delete", TaskId = id });
            await _wsService.Broadcast(message);

            return NoContent();
        }

        // 6. Отправка письма вручную (SMTP)
        [HttpPost("send-email")]
        public async Task<IActionResult> SendEmail(int taskId, string recipientEmail)
        {
            var task = await _context.Tasks.FindAsync(taskId);
            if (task == null) return NotFound();

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("ToDoList App", _smtpSettings.SenderEmail));
                message.To.Add(new MailboxAddress("", recipientEmail));
                message.Subject = $"Напоминание о задаче: {task.Title}";

                var bodyBuilder = new BodyBuilder
                {
                    TextBody = $"Не забудьте выполнить задачу: {task.Title}\nОписание: {task.Description}"
                };
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, MailKit.Security.SecureSocketOptions.StartTls);
                    await client.AuthenticateAsync(_smtpSettings.SenderEmail, _smtpSettings.SenderPassword);

                    _logger.LogInformation("Отправка письма на {RecipientEmail} для задачи {TaskId}", recipientEmail, taskId);
                    await client.SendAsync(message);
                    _logger.LogInformation("Письмо успешно отправлено на {RecipientEmail} для задачи {TaskId}", recipientEmail, taskId);

                    await client.DisconnectAsync(true);
                }

                return Ok("Письмо отправлено!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке письма на {RecipientEmail} для задачи {TaskId}", recipientEmail, taskId);
                return StatusCode(500, "Ошибка при отправке письма: " + ex.Message);
            }
        }

        // 7. Проверка входящих писем через IMAP
        [HttpGet("check-inbox-imap")]
        public async Task<IActionResult> CheckInboxImap()
        {
            try
            {
                _logger.LogInformation("Начало проверки входящих писем через IMAP");

                using (var client = new ImapClient())
                {
                    await client.ConnectAsync("imap.mail.ru", 993, true);
                    _logger.LogInformation("Подключение к IMAP-серверу imap.mail.ru:993 успешно");

                    await client.AuthenticateAsync(_smtpSettings.SenderEmail, _smtpSettings.SenderPassword);
                    _logger.LogInformation("Аутентификация для {SenderEmail} успешна", _smtpSettings.SenderEmail);

                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadOnly);
                    _logger.LogInformation("Папка Входящие открыта");

                    var messages = await inbox.FetchAsync(0, -1, MessageSummaryItems.Envelope);
                    var result = messages.Take(5).Select(m => new
                    {
                        Subject = m.Envelope.Subject,
                        From = m.Envelope.From.ToString(),
                        Date = m.Envelope.Date
                    }).ToList();

                    _logger.LogInformation("Найдено {Count} писем", result.Count);
                    await client.DisconnectAsync(true);

                    return Ok(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке входящих писем через IMAP");
                return StatusCode(500, "Ошибка при проверке входящих: " + ex.Message);
            }
        }

        // 8. Проверка входящих писем через POP3
        [HttpGet("check-inbox-pop3")]
        public async Task<IActionResult> CheckInboxPop3()
        {
            try
            {
                _logger.LogInformation("Начало проверки входящих писем через POP3");

                using (var client = new Pop3Client())
                {
                    await client.ConnectAsync("pop.mail.ru", 995, true);
                    _logger.LogInformation("Подключение к POP3-серверу pop.mail.ru:995 успешно");

                    await client.AuthenticateAsync(_smtpSettings.SenderEmail, _smtpSettings.SenderPassword);
                    _logger.LogInformation("Аутентификация для {SenderEmail} успешна", _smtpSettings.SenderEmail);

                    int messageCount = await client.GetMessageCountAsync();
                    _logger.LogInformation("Найдено {MessageCount} писем", messageCount);

                    var subjects = new List<object>();
                    for (int i = 0; i < Math.Min(5, messageCount); i++)
                    {
                        var message = await client.GetMessageAsync(i);
                        subjects.Add(new
                        {
                            Subject = message.Subject,
                            From = message.From.ToString(),
                            Date = message.Date
                        });
                    }

                    _logger.LogInformation("Загружено {Count} писем", subjects.Count);
                    await client.DisconnectAsync(true);

                    return Ok(subjects);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке входящих писем через POP3");
                return StatusCode(500, "Ошибка при проверке входящих: " + ex.Message);
            }
        }

        // Вспомогательный метод для отправки писем через SMTP
        private async Task SendMailAsync(string taskTitle, string recipientEmail, string subject)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("ToDoList App", _smtpSettings.SenderEmail));
                message.To.Add(new MailboxAddress("", recipientEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder
                {
                    TextBody = $"Задача: {taskTitle}"
                };
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, MailKit.Security.SecureSocketOptions.StartTls);
                    await client.AuthenticateAsync(_smtpSettings.SenderEmail, _smtpSettings.SenderPassword);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке письма на {RecipientEmail}", recipientEmail);
                throw;
            }
        }
    }
}