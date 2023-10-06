using Azure.Communication.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace chwhatsappgpt;

public class SendEmail
{
    [FunctionName("SendEmail")]
    public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
    ILogger log)
    {
        log.LogInformation("Enviar Email");
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        var email = PreparaCorpoEmail(requestBody);        

        EmailClient emailClient = new EmailClient(Environment.GetEnvironmentVariable("URL_SERVICE_EMAIL"));
                
        var emailSendOperation = await emailClient.SendAsync(
                                                    Azure.WaitUntil.Completed,
                                                    email);

        EmailSendResult statusMonitor = emailSendOperation.Value;

        log.LogInformation($"Email Sent. Status = {emailSendOperation.Value.Status}");

        string operationId = emailSendOperation.Id;
        log.LogInformation($"Email operation id = {operationId}");

        return new OkObjectResult("");
    }

    private static EmailMessage PreparaCorpoEmail(string email)
    {
        var obj = JsonConvert.DeserializeObject<Gerenciamento>(email);
        var sender = "donotreply@codehead.com.br";

        var subject = "Fatura";
        var htmlContent = "<html><body><h1>Fatura</h1><br/><h4>Este e-mail contém sua fatura.</h4><p>Baixe nosso aplicativo nas lojas.</p></body></html>";
        var emailMessage = new EmailMessage(sender, obj.Email, new EmailContent(subject) { Html = htmlContent});

        if (!string.IsNullOrEmpty(obj.ArquivoBase64)) { 
            var pdfBinaryData = BinaryData.FromStream(new MemoryStream(Convert.FromBase64String(obj.ArquivoBase64)));
            emailMessage.Attachments.Add(new EmailAttachment("Fatura.pdf", "application/pdf", pdfBinaryData));
        }
        
        return emailMessage;
    }

}
