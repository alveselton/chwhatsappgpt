using Azure.Communication.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Microsoft.WindowsAzure.Storage.Queue.Protocol;
using System.Text.Json;
using System.Net.Http;
using System.Net;
using Newtonsoft.Json;

namespace chwhatsappgpt;

public static class WorkerGerenciamento
{
    private static string _URL_MENSAGERIA = "amqp://jwczbtul:vmX6DvLV2k-ZawR_PTyV6drJk5m5QI8b@jackal.rmq.cloudamqp.com/jwczbtul";

    [FunctionName("WorkerGerenciamento")]
    public async static Task Run(
        [TimerTrigger("0 0/5 * * * *")] TimerInfo myTimer,
        ILogger log)
    {
        try
        {
            log.LogInformation("Início do worker");

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_URL_MENSAGERIA)
            };

            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var queueName = "Gerenciamento";

                channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

                var consumer = new EventingBasicConsumer(channel);

                consumer.Received += (model, ea) =>
                {
                    var messageBody = Encoding.UTF8.GetString(ea.Body.ToArray());
                    log.LogInformation($"Mensagem recebida da fila RabbitMQ: {messageBody}");


                    var queueMessage = JsonSerializer.Deserialize<Gerenciamento>(messageBody);

                    channel.BasicAck(ea.DeliveryTag, false);
                };

                channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            }
        }
        catch (Exception ex)
        {
            log.LogError($"Erro ao consumir mensagem da fila RabbitMQ: {ex.Message}");
        }
    }

    private async static Task<bool> Enviar(Gerenciamento gerenciamento, ILogger log)
    {
        try
        {
            log.LogInformation("Gerenciamento *****");
            var client = new HttpClient();
            var response = await client
                .PostAsync(
                "http://localhost:7038/api/SendEmail",
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
