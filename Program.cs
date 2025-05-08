using Microsoft.EntityFrameworkCore;
using ToDoListAPI.Data;
using ToDoListAPI.Models;
using ToDoListAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Принудительно задаем порт
var port = Environment.GetEnvironmentVariable("PORT") ?? "8090";
builder.WebHost.UseUrls($"http://+:{port}");

// Подключение к базе данных
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", builder =>
    {
        builder.WithOrigins("http://localhost:8091")
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Добавляем контроллеры и Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Регистрируем SmtpSettings из appsettings.json
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));

// Регистрируем сервис WebSocket
builder.Services.AddSingleton<WebSocketService>();

var app = builder.Build();

// Включаем поддержку WebSocket
app.UseWebSockets();

// Настраиваем endpoint для WebSocket
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var wsService = context.RequestServices.GetRequiredService<WebSocketService>();
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await wsService.HandleWebSocket(webSocket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

// Настраиваем middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ToDoListAPI v1");
    c.RoutePrefix = "swagger";
});

// Конвейер middleware
app.UseRouting();
app.UseCors("AllowBlazor");
app.UseAuthorization();
app.MapControllers();

app.Run();