using Microsoft.EntityFrameworkCore;
using ToDoListAPI.Data;
using ToDoListAPI.Models;
using ToDoListAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// ������������� ������ ����
var port = Environment.GetEnvironmentVariable("PORT") ?? "8090";
builder.WebHost.UseUrls($"http://+:{port}");

// ����������� � ���� ������
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ��������� CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazor", builder =>
    {
        builder.WithOrigins("http://localhost:8091")
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// ��������� ����������� � Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ������������ SmtpSettings �� appsettings.json
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));

// ������������ ������ WebSocket
builder.Services.AddSingleton<WebSocketService>();

var app = builder.Build();

// �������� ��������� WebSocket
app.UseWebSockets();

// ����������� endpoint ��� WebSocket
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

// ����������� middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ToDoListAPI v1");
    c.RoutePrefix = "swagger";
});

// �������� middleware
app.UseRouting();
app.UseCors("AllowBlazor");
app.UseAuthorization();
app.MapControllers();

app.Run();