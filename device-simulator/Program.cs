using device_simulator;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<MQTTService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// 測試 MQTT 連線
app.MapPost("/send-telemetry", async (double temperature, int humidity, MQTTService mqttService) =>
{
    await mqttService.PublishTelemetryAsync(temperature, humidity);
    return Results.Ok(new { message = "遙測數據已發送", temperature, humidity });
});

// 從 device-001 到 device-1000 各發送一次遙測數據
app.MapPost("/send-telemetry-all-devices", async (double temperature, int humidity, MQTTService mqttService) =>
{
    await mqttService.PublishTelemetryForAllDevicesAsync(temperature, humidity);
    return Results.Ok(new { message = "已發送 1000 個設備的遙測數據", temperature, humidity, deviceCount = 1000 });
});

app.Run();
