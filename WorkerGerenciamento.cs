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
    public static void RunAsync(
        [TimerTrigger("* 0/5 * * * *")] TimerInfo myTimer,
        ILogger log)
    {
        log.LogInformation("Início do worker");
        LerFila(log);        
        log.LogInformation("Final fila.....");
    }

    private static void LerFila(ILogger log)
    {
        log.LogInformation("LerFila.....");
        var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("URL_GERENCIAMENTO")) };

        var connection = factory.CreateConnection();
        var channel = connection.CreateModel();

        channel.QueueDeclare(
                            queue: "gerenciamento",
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);

        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new EventingBasicConsumer(channel);

        consumer.Received += (model, ea) =>
        {
            log.LogInformation("Tem Msg");

            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var msg = JsonConvert.DeserializeObject<Gerenciamento>(message);
                var responseEnviar = Enviar(msg, log).Result;

                if (responseEnviar)
                    channel.BasicAck(ea.DeliveryTag, false);
                else
                    channel.BasicNack(ea.DeliveryTag, false, true);
            }
            catch (Exception ex)
            {
                channel.BasicNack(ea.DeliveryTag, false, true);
                log.LogError(ex.Message, ex);
            }
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
