using bridge_service;

var builder = Host.CreateApplicationBuilder(args);

// µù¥U°t¸m
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("MqttSettings"));
builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("KafkaSettings"));

// µù¥U Worker Service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
