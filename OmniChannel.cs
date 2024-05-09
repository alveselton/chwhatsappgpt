using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace chwhatsappgpt;

public class OmniChannel
{
    private static readonly string _urlMensageria = "";

    [FunctionName("SendGerenciamento")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
    ILogger log)
    {
        try
        {
            log.LogInformation("Inicio Requisicao");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var gerenciamento = JsonConvert.DeserializeObject<Gerenciamento>(requestBody);

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_urlMensageria)
            };

            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var queueName = "gerenciamento";

                channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

                var messageBody = JsonConvert.SerializeObject(gerenciamento);
                var body = Encoding.UTF8.GetBytes(messageBody);
                channel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: null, body: body);

                log.LogInformation($"Mensagem enviada para a fila RabbitMQ: {messageBody}");
            }

            return new OkObjectResult("");
        }
        catch (Exception ex)
        {
            log.LogError($"Erro ao enviar mensagem para a fila RabbitMQ: {ex.Message}");
            return new BadRequestObjectResult("Erro ao enviar mensagem para a fila RabbitMQ.");
        }
    }

    [FunctionName("SendGerenciamentoDlq")]
    public static async Task<IActionResult> RunDlQ(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
    ILogger log)
    {
        try
        {
            log.LogInformation("Inicio Requisicao");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var gerenciamento = JsonConvert.DeserializeObject<Gerenciamento>(requestBody);

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_urlMensageria)
            };

            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var queueName = "Gerenciamento-dlq";

                channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

                var messageBody = JsonConvert.SerializeObject(gerenciamento);
                var body = Encoding.UTF8.GetBytes(messageBody);
                channel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: null, body: body);

                log.LogInformation($"Mensagem enviada para a fila RabbitMQ: {messageBody}");
            }

            return new OkObjectResult("");
        }
        catch (Exception ex)
        {
            log.LogError($"Erro ao enviar mensagem para a fila RabbitMQ: {ex.Message}");
            return new BadRequestObjectResult("Erro ao enviar mensagem para a fila RabbitMQ.");
        }
    }
}
