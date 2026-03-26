using telemetry_processor;


var builder = Host.CreateApplicationBuilder(args);

// °t¸m InfluxDB
var influxDBConfig = builder.Configuration.GetSection("InfluxDB").Get<InfluxDBConfig>() ?? new InfluxDBConfig();
builder.Services.AddSingleton(influxDBConfig);
builder.Services.AddSingleton<IInfluxDBService, InfluxDBService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();


