using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace chwhatsappgpt;

public static class WorkerGerenciamento
{
    [FunctionName("WorkerGerenciamento")]
    public static async Task RunAsync(
        [TimerTrigger("0 0/1 * * * *")] TimerInfo myTimer,
        ILogger log)
    {
        log.LogInformation(Environment.GetEnvironmentVariable("URL_GERENCIAMENTO"));
        log.LogInformation("Início do worker");
        await LerFila(log);        
        log.LogInformation("Final fila.....");
    }

    private async static Task LerFila(ILogger log)
    {
        var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("URL_GERENCIAMENTO")) };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(
                            queue: "gerenciamento",
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);

        var consumer = new EventingBasicConsumer(channel);

        consumer.Received += async (model, ea) =>
        {
            log.LogInformation("Tem Msg");
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var msg = JsonConvert.DeserializeObject<Gerenciamento>(message);

            var responseEnviar = await Enviar(msg, log);

            if (responseEnviar)
                channel.BasicAck(ea.DeliveryTag, false);
            else
                channel.BasicNack(ea.DeliveryTag, false, false);
        };

        channel.BasicConsume("gerenciamento",
                             false,
                             consumer);
    }

    private async static Task<bool> Enviar(Gerenciamento gerenciamento, ILogger log)
    {
        try
        {
            log.LogInformation("Gerenciamento *****");
            var client = new HttpClient();
            var response = await client
                .PostAsync(
                Environment.GetEnvironmentVariable("URL_EMAIL"),
                new StringContent(JsonConvert.SerializeObject(gerenciamento),
                Encoding.UTF8,
                "application/json"));
            
            if (response.StatusCode == HttpStatusCode.OK)
                return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex.Message);
        }

        return false;
    }

}
