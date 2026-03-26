using alert_processor;
using Confluent.Kafka;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();
