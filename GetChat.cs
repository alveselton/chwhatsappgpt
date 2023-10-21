using chwhatsappgpt.Interface;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML;
using Twilio.TwiML.Messaging;
using static chwhatsappgpt.GetGpt;

namespace chwhatsappgpt;

public static class GetChat
{
    private static readonly Lazy<MongoClient> lazyClient = new(InitializeMongoClient);
    private static readonly MongoClient client = lazyClient.Value;

    private static readonly string apiUrl = "https://api.openai.com/v1/engines/text-davinci-003/completions";
    private static readonly string endpoint = "https://api.openai.com/v1/chat/completions";

    static GetChat()
    {
        string accountSid = "AC826847d039c4e83a67cc1353830758e0";
        string authToken = "45f3c10c773b753e0eef80ddedf28744";
        TwilioClient.Init(accountSid, authToken);
    }

    [FunctionName("GetChat")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("Inicio Chat");
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        log.LogInformation(requestBody);

        var parameters = parametrosPesquisa(requestBody, log);

        StringBuilder contentGpt = PromptGpt(parameters);

        var response = await GenerateResponseGptTurbo(contentGpt.ToString(), log);

        if (response.choices.Count > 0)
        {
            log.LogInformation("Fluxo " + response.choices[0].message.content);

            var responseTratado = ConverterValor(RemoverAcentos(response.choices[0].message.content), log);
            log.LogInformation("response - " + Newtonsoft.Json.JsonConvert.SerializeObject(responseTratado));

            //SAUDACAO
            var saudacao = SeSaudacao(responseTratado, parameters);
            if (saudacao.Saudacao && saudacao.IsEnviar)
            {
                log.LogInformation("Saudacao");
                return new OkObjectResult(saudacao.Mensagem);
            }

            //FATURA
            var fatura = await SeFatura(responseTratado, parameters, log);
            if (fatura.Fatura && saudacao.IsEnviar && !responseTratado.FaturaAberto)
            {
                log.LogInformation("Fatura " + fatura.ToString());
                return new OkObjectResult(fatura.Mensagem);
            }

            //FATURA EM ABERTO
            var faturaAberto = await SeFaturaAberto(responseTratado, parameters, log);
            if (faturaAberto.FaturaAberto && faturaAberto.IsEnviar)
            {
                log.LogInformation("Fatura em aberto " + faturaAberto.Mensagem);
                return new OkObjectResult(faturaAberto.Mensagem);
            }

            log.LogInformation("Final");
            return new OkObjectResult(responseTratado);
        }
        else
        {
            return new BadRequestObjectResult("Erro na chamada à API do OpenAI.");
        }
    }

    private static async Task SendMessageWhatsapp(string to, string from, string msg, ILogger log)
    {
        try
        {
            log.LogInformation($"{to} - {from} - {msg}");

            var messageSend = MessageResource.Create(
                body: msg,
                to: new Twilio.Types.PhoneNumber(from),
                from: new Twilio.Types.PhoneNumber(to)
            );

            log.LogInformation(messageSend.Sid);
        }
        catch (Exception ex)
        {
            log.LogInformation(ex.Message);
            log.LogInformation(ex.StackTrace);
            throw;
        }
    }

    private static StringBuilder PromptGpt(Parameters parameters)
    {
        var contentGpt = new StringBuilder();
        contentGpt.Append("No 'Texto' tem alguma data? alguma informacao se é sobre fatura? Tem pedido de informacao sobre produto? Tem pedido de cancelamento?");
        contentGpt.Append("Tem informacao de roubo ou perda de cartao? ");
        contentGpt.Append("Tem algum pedido de informacao de Cartao? ");
        contentGpt.Append("Tem pedido de informacao de Cadastro? ");
        contentGpt.Append("Informacao sobre nao reconhecer Compras? ");
        contentGpt.Append("O texto contem saudacao inicial de conversa? ");
        contentGpt.Append("\nObservacao:");
        contentGpt.Append("\n- Data precisa que seja formatada em MM/YYYY;");
        contentGpt.Append("\n- Se tiver no texto \"fatura do meu cartao\" a fatura= sim e cartao = nao;");
        contentGpt.Append("\n- Se tiver no texto \"saldo cartao\" a fatura = nao e cartao = sim e Saldo-Cartao = sim;");
        contentGpt.Append("\n- Se tiver no texto \"saldo fatura\" a fatura = sim e cartao = nao e Saldo-Cartao = nao;");
        contentGpt.Append("\n- Se tiver no texto \"fatura aberta\" a fatura = nao e cartao = nao e Saldo-Cartao = nao e Fatura-aberto = sim;");
        contentGpt.Append("\n- Nao pode haver acentuacao;");
        contentGpt.Append("\n- Preciso que o dado sejam resumido.\n\n");
        contentGpt.Append($"\n- Data: <<valor>>\n- Fatura: Sim ou Nao\n- Produto: Sim ou Nao\n- Roubo ou Perda de Cartao: Sim ou Nao\n- Cancelamento: Sim ou Nao\n- Cartao: Sim ou Nao\n- Cadastro: Sim ou Nao\n- Nao-Reconhece-Compras: Sim ou Nao\n- Saudacao: Sim ou Nao\n- Saldo-Cartao: Sim ou Nao\n- Fatura-aberto: Sim ou Nao\n\nTexto: \"{parameters.Question}\"");
        return contentGpt;
    }

    private static async Task<OpenAIResponse> GenerateResponse(string question)
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {Environment.GetEnvironmentVariable("APIKEY_GPT")}");

                var requestData = new
                {
                    prompt = $"{question}",
                    max_tokens = 500,
                };

                var jsonRequest = Newtonsoft.Json.JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var openAIResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenAIResponse>(jsonResponse);
                    return openAIResponse;
                }
                else
                {
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao gerar resposta: {ex.Message}");
            return null;
        }
    }

    private static async Task<OpenAIResponse> GenerateResponseGptTurbo(string question, ILogger log)
    {
        log.LogInformation("Inicio GPT");
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("APIKEY_GPT"));

        var parameters = new
        {
            model = "gpt-3.5-turbo",
            messages = new[] { new { role = "user", content = question }, },
            max_tokens = 500,
            temperature = 0.57f,
        };

        log.LogInformation("Request Gpt - " + Newtonsoft.Json.JsonConvert.SerializeObject(parameters));

        var response = await client.PostAsync(endpoint, new StringContent(JsonConvert.SerializeObject(parameters), Encoding.UTF8, "application/json"));

        // Read the response
        var responseContent = await response.Content.ReadAsStringAsync();

        var responseObject = JsonConvert.DeserializeObject<OpenAIResponse>(responseContent);
        log.LogInformation("GPT - " + Newtonsoft.Json.JsonConvert.SerializeObject(responseObject));
        return responseObject;
    }

    private static ResponseInfo ConverterValor(string response, ILogger log)
    {
        var responseText = response;

        // Divida o texto em linhas para extrair os valores
        var lines = responseText.Split('\n');
        var responseInfo = new ResponseInfo();

        foreach (var line in lines)
        {
            log.LogInformation(line);

            var parts = line.Split(':');
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key)
                {
                    case "- Data":
                    case "Data":
                        responseInfo.Data = value;
                        break;
                    case "- Fatura":
                    case "Fatura":
                        responseInfo.Fatura = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Produto":
                    case "Produto":
                        responseInfo.Produto = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Roubo-Perda-Cartao":
                    case "Roubo-Perda-Cartao":
                        responseInfo.RouboPerdaCartao = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Cancelamento":
                    case "Cancelamento":
                        responseInfo.Cancelamento = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Cartao":
                    case "Cartao":
                        responseInfo.Cartao = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Cadastro":
                    case "Cadastro":
                        responseInfo.Cadastro = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Nao-Reconhece-Compras":
                    case "Nao-Reconhece-Compras":
                        responseInfo.NaoReconheceCompras = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Saudacao":
                    case "Saudacao":
                        responseInfo.Saudacao = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Fatura-aberto":
                    case "Fatura-aberto":
                        responseInfo.FaturaAberto = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    default:
                        // Ignorar chaves desconhecidas
                        break;
                }
            }
        }

        return responseInfo;
    }

    private static MongoClient InitializeMongoClient()
    {
        return new MongoClient(Environment.GetEnvironmentVariable("MONGODB_ATLAS_URI"));
    }

    public static Parameters parametrosPesquisa(string parameters, ILogger log)
    {
        if (parameters.Contains('&'))
        {
            var requestBody = parameters.Split('&')?
                .Select(param => param.Split('='))?
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

            var obj = new Parameters();

            if (requestBody.ContainsKey("ProfileName"))
            {
                obj.ProfileName = requestBody["ProfileName"]?.Replace("+", " ") ?? " =)";
            }

            if (requestBody.ContainsKey("SmsMessageSid"))
                obj.SmsMessageSid = requestBody["SmsMessageSid"];

            if (requestBody.ContainsKey("waId"))
                obj.WaId = requestBody["waId"];

            if (requestBody.ContainsKey("Body"))
            {
                obj.Body = requestBody["Body"];
                obj.Question = requestBody["Body"];
            }

            if (requestBody.ContainsKey("fromreceived"))
                obj.Fromreceived = requestBody["fromreceived"];

            if (requestBody.ContainsKey("To"))
                obj.To = requestBody["To"];

            if (requestBody.ContainsKey("From"))
                obj.From = requestBody["From"];

            obj.Parametros = requestBody;
            obj.User = obj?.Fromreceived?.Split(":")[1];
            log.LogInformation("parametrosPesquisa - " + Newtonsoft.Json.JsonConvert.SerializeObject(obj));
            return obj;
        }
        else
        {
            var obj = JsonConvert.DeserializeObject<Parameters>(parameters);
            log.LogInformation("parametrosPesquisa - " + Newtonsoft.Json.JsonConvert.SerializeObject(obj));
            return obj;
        }
    }

    //SAUDACAO
    private static ResponseInfo SeSaudacao(ResponseInfo responseInfo, Parameters parametros)
    {
        if (responseInfo.Saudacao &&
            !new[] { responseInfo.Fatura, responseInfo.Produto, responseInfo.RouboPerdaCartao, responseInfo.Cancelamento, responseInfo.Cartao, responseInfo.Cadastro, responseInfo.NaoReconheceCompras }.Any(field => field))
        {
            responseInfo.Mensagem = $"Olá, {parametros.ProfileName}! Tudo bem? \u263A\n\nPara que possamos ajuda-lo você pode solicitar o que desejar em poucas palavras.\n\nExemplo:\n🔊👉 Minha fatura de Janeiro de 2023;\n🔊👉 Saldo do meu cartão;\n🔊👉 Quais os produtos que posso adquirir;\n🔊👉 Cancelar cartão;\n🔊👉 Nao Reconheço uma compra;\n🔊👉 Bloquear meu cartão;";
            responseInfo.IsEnviar = true;

            return responseInfo;
        }

        return responseInfo;
    }

    //FATURA
    //var gerenciamento = await EnviarMensageria(new Gerenciamento() { Canal = "Email", Email = "alveselton@gmail.com", Mensagem = "Segue sua fatura", ArquivoBase64 = "JVBERi0xLjcNCiW1tbW1DQoxIDAgb2JqDQo8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFIvTGFuZyhwdCkgL1N0cnVjdFRyZWVSb290IDE1IDAgUi9NYXJrSW5mbzw8L01hcmtlZCB0cnVlPj4vTWV0YWRhdGEgMjcgMCBSL1ZpZXdlclByZWZlcmVuY2VzIDI4IDAgUj4+DQplbmRvYmoNCjIgMCBvYmoNCjw8L1R5cGUvUGFnZXMvQ291bnQgMS9LaWRzWyAzIDAgUl0gPj4NCmVuZG9iag0KMyAwIG9iag0KPDwvVHlwZS9QYWdlL1BhcmVudCAyIDAgUi9SZXNvdXJjZXM8PC9Gb250PDwvRjEgNSAwIFIvRjIgMTIgMCBSPj4vRXh0R1N0YXRlPDwvR1MxMCAxMCAwIFIvR1MxMSAxMSAwIFI+Pi9Qcm9jU2V0Wy9QREYvVGV4dC9JbWFnZUIvSW1hZ2VDL0ltYWdlSV0gPj4vTWVkaWFCb3hbIDAgMCA1OTUuMyA4NDEuOV0gL0NvbnRlbnRzIDQgMCBSL0dyb3VwPDwvVHlwZS9Hcm91cC9TL1RyYW5zcGFyZW5jeS9DUy9EZXZpY2VSR0I+Pi9UYWJzL1MvU3RydWN0UGFyZW50cyAwPj4NCmVuZG9iag0KNCAwIG9iag0KPDwvRmlsdGVyL0ZsYXRlRGVjb2RlL0xlbmd0aCAxOTQ+Pg0Kc3RyZWFtDQp4nJWOuwrCQBBF+4X9h1saIZuZNZtsICyY+EDRQg1YiIWFpjLEx//jRi3UzmkOl7nMHESb9tAgz6NlORuBosWhqdFr72GxDpxDMSpxkYIUdWNtyiCYzKgBbMwqw/UoxbaPRoqikiKaMJhRnaToigSGNYq1QZqQMqjOvjPdMKG++auoX5HfcSrFLifSiTOZJ8Uu7ZjELhx0tC7Unmb43O1RzaUY+7crKf6S1L+SzJmy+kPyqfYS6iH4+oTxssQDn8w9+Q0KZW5kc3RyZWFtDQplbmRvYmoNCjUgMCBvYmoNCjw8L1R5cGUvRm9udC9TdWJ0eXBlL1R5cGUwL0Jhc2VGb250L0JDREVFRStDYWxpYnJpL0VuY29kaW5nL0lkZW50aXR5LUgvRGVzY2VuZGFudEZvbnRzIDYgMCBSL1RvVW5pY29kZSAyMyAwIFI+Pg0KZW5kb2JqDQo2IDAgb2JqDQpbIDcgMCBSXSANCmVuZG9iag0KNyAwIG9iag0KPDwvQmFzZUZvbnQvQkNERUVFK0NhbGlicmkvU3VidHlwZS9DSURGb250VHlwZTIvVHlwZS9Gb250L0NJRFRvR0lETWFwL0lkZW50aXR5L0RXIDEwMDAvQ0lEU3lzdGVtSW5mbyA4IDAgUi9Gb250RGVzY3JpcHRvciA5IDAgUi9XIDI1IDAgUj4+DQplbmRvYmoNCjggMCBvYmoNCjw8L09yZGVyaW5nKElkZW50aXR5KSAvUmVnaXN0cnkoQWRvYmUpIC9TdXBwbGVtZW50IDA+Pg0KZW5kb2JqDQo5IDAgb2JqDQo8PC9UeXBlL0ZvbnREZXNjcmlwdG9yL0ZvbnROYW1lL0JDREVFRStDYWxpYnJpL0ZsYWdzIDMyL0l0YWxpY0FuZ2xlIDAvQXNjZW50IDc1MC9EZXNjZW50IC0yNTAvQ2FwSGVpZ2h0IDc1MC9BdmdXaWR0aCA1MjEvTWF4V2lkdGggMTc0My9Gb250V2VpZ2h0IDQwMC9YSGVpZ2h0IDI1MC9TdGVtViA1Mi9Gb250QkJveFsgLTUwMyAtMjUwIDEyNDAgNzUwXSAvRm9udEZpbGUyIDI0IDAgUj4+DQplbmRvYmoNCjEwIDAgb2JqDQo8PC9UeXBlL0V4dEdTdGF0ZS9CTS9Ob3JtYWwvY2EgMT4+DQplbmRvYmoNCjExIDAgb2JqDQo8PC9UeXBlL0V4dEdTdGF0ZS9CTS9Ob3JtYWwvQ0EgMT4+DQplbmRvYmoNCjEyIDAgb2JqDQo8PC9UeXBlL0ZvbnQvU3VidHlwZS9UcnVlVHlwZS9OYW1lL0YyL0Jhc2VGb250L0JDREZFRStDYWxpYnJpL0VuY29kaW5nL1dpbkFuc2lFbmNvZGluZy9Gb250RGVzY3JpcHRvciAxMyAwIFIvRmlyc3RDaGFyIDMyL0xhc3RDaGFyIDMyL1dpZHRocyAyNiAwIFI+Pg0KZW5kb2JqDQoxMyAwIG9iag0KPDwvVHlwZS9Gb250RGVzY3JpcHRvci9Gb250TmFtZS9CQ0RGRUUrQ2FsaWJyaS9GbGFncyAzMi9JdGFsaWNBbmdsZSAwL0FzY2VudCA3NTAvRGVzY2VudCAtMjUwL0NhcEhlaWdodCA3NTAvQXZnV2lkdGgg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WE1ML0xlbmd0aCAzMDg5Pj4NCnN0cmVhbQ0KPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0wTXBDZWhpSHpyZVN6TlRjemtjOWQiPz48eDp4bXBtZXRhIHhtbG5zOng9ImFkb2JlOm5zOm1ldGEvIiB4OnhtcHRrPSIzLjEtNzAxIj4KPHJkZjpSREYgeG1sbnM6cmRmPSJodHRwOi8vd3d3LnczLm9yZy8xOTk5LzAyLzIyLXJkZi1zeW50YXgtbnMjIj4KPHJkZjpEZXNjcmlwdGlvbiByZGY6YWJvdXQ9IiIgIHhtbG5zOnBkZj0iaHR0cDovL25zLmFkb2JlLmNvbS9wZGYvMS4zLyI+CjxwZGY6UHJvZHVjZXI+TWljcm9zb2Z0wq4gV29yZCBwYXJhIE1pY3Jvc29mdCAzNjU8L3BkZjpQcm9kdWNlcj48L3JkZjpEZXNjcmlwdGlvbj4KPHJkZjpEZXNjcmlwdGlvbiByZGY6YWJvdXQ9IiIgIHhtbG5zOmRjPSJodHRwOi8vcHVybC5vcmcvZGMvZWxlbWVudHMvMS4xLyI+CjxkYzpjcmVhdG9yPjxyZGY6U2VxPjxyZGY6bGk+RWx0b24gQWx2ZXM8L3JkZjpsaT48L3JkZjpTZXE+PC9kYzpjcmVhdG9yPjwvcmRmOkRlc2NyaXB0aW9uPgo8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91dD0iIiAgeG1sbnM6eG1wPSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvIj4KPHhtcDpDcmVhdG9yVG9vbD5NaWNyb3NvZnTCriBXb3JkIHBhcmEgTWljcm9zb2Z0IDM2NTwveG1wOkNyZWF0b3JUb29sPjx4bXA6Q3JlYXRlRGF0ZT4yMDIzLTEwLTAzVDAzOjM0OjEzLTAzOjAwPC94bXA6Q3JlYXRlRGF0ZT48eG1wOk1vZGlmeURhdGU+MjAyMy0xMC0wM1QwMzozNDoxMy0wMzowMDwveG1wOk1vZGlmeURhdGU+PC9yZGY6RGVzY3JpcHRpb24+CjxyZGY6RGVzY3JpcHRpb24gcmRmOmFib3V0PSIiICB4bWxuczp4bXBNTT0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wL21tLyI+Cjx4bXBNTTpEb2N1bWVudElEPnV1aWQ6MTI0MDZCMzUtNjgxNC00RkY1LUI3MjMtMTU4N0M1M0Y1ODAxPC94bXBNTTpEb2N1bWVudElEPjx4bXBNTTpJbnN0YW5jZUlEPnV1aWQ6MTI0MDZCMzUtNjgxNC00RkY1LUI3MjMtMTU4N0M1M0Y1ODAxPC94bXBNTTpJbnN0YW5jZUlEPjwvcmRmOkRlc2NyaXB0aW9uPgogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgCiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKPC9yZGY6UkRGPjwveDp4bXBtZXRhPjw/eHBhY2tldCBlbmQ9InciPz4NCmVuZHN0cmVhbQ0KZW5kb2JqDQoyOCAwIG9iag0KPDwvRGlzcGxheURvY1RpdGxlIHRydWU+Pg0KZW5kb2JqDQoyOSAwIG9iag0KPDwvVHlwZS9YUmVmL1NpemUgMjkvV1sgMSA0IDJdIC9Sb290IDEgMCBSL0luZm8gMTQgMCBSL0lEWzwzNTZCNDAxMjE0NjhGNTRGQjcyMzE1ODdDNTNGNTgwMT48MzU2QjQwMTIxNDY4RjU0RkI3MjMxNTg3QzUzRjU4MDE+XSAvRmlsdGVyL0ZsYXRlRGVjb2RlL0xlbmd0aCAxMDk+Pg0Kc3RyZWFtDQp4nC3OLRZAUBQE4Hn+D0mxAiuQbcEiCKKkyI6g2oCgadaj2ITMM+OG+5WZey5g53mM3SnwsYmTmJu4RoxiJV4pJuL3YhcXCWYSDoBjb2ZwhCs84Qsj/mRge9HCepyTpCW1PmsKUZHuAF5x+A8+DQplbmRzdHJlYW0NCmVuZG9iag0KeHJlZg0KMCAzMA0KMDAwMDAwMDAxNSA2NTUzNSBmDQowMDAwMDAwMDE3IDAwMDAwIG4NCjAwMDAwMDAxNjMgMDAwMDAgbg0KMDAwMDAwMDIxOSAwMDAwMCBuDQowMDAwMDAwNTAxIDAwMDAwIG4NCjAwMDAwMDA3NjkgMDAwMDAgbg0KMDAwMDAwMDg5OSAwMDAwMCBuDQowMDAwMDAwOTI3IDAwMDAwIG4NCjAwMDAwMDEwODQgMDAwMDAgbg0KMDAwMDAwMTE1NyAwMDAwMCBuDQowMDAwMDAxMzk2IDAwMDAwIG4NCjAwMDAwMDE0NTAgMDAwMDAgbg0KMDAwMDAwMTUwNCAwMDAwMCBuDQowMDAwMDAxNjczIDAwMDAwIG4NCjAwMDAwMDE5MTMgMDAwMDAgbg0KMDAwMDAwMDAxNiA2NTUzNSBmDQowMDAwMDAwMDE3IDY1NTM1IGYNCjAwMDAwMDAwMTggNjU1MzUgZg0KMDAwMDAwMDAxOSA2NTUzNSBmDQowMDAwMDAwMDIwIDY1NTM1IGYNCjAwMDAwMDAwMjEgNjU1MzUgZg0KMDAwMDAwMDAyMiA2NTUzNSBmDQowMDAwMDAwMDAwIDY1NTM1IGYNCjAwMDAwMDI1OTYgMDAwMDAgbg0KMDAwMDAwMjkxOCAwMDAwMCBuDQowMDAwMDI1MDUxIDAwMDAwIG4NCjAwMDAwMjUxMzggMDAwMDAgbg0KMDAwMDAyNTE2NSAwMDAwMCBuDQowMDAwMDI4MzM3IDAwMDAwIG4NCjAwMDAwMjgzODIgMDAwMDAgbg0KdHJhaWxlcg0KPDwvU2l6ZSAzMC9Sb290IDEgMCBSL0luZm8gMTQgMCBSL0lEWzwzNTZCNDAxMjE0NjhGNTRGQjcyMzE1ODdDNTNGNTgwMT48MzU2QjQwMTIxNDY4RjU0RkI3MjMxNTg3QzUzRjU4MDE+XSA+Pg0Kc3RhcnR4cmVmDQoyODY5Mg0KJSVFT0YNCnhyZWYNCjAgMA0KdHJhaWxlcg0KPDwvU2l6ZSAzMC9Sb290IDEgMCBSL0luZm8gMTQgMCBSL0lEWzwzNTZCNDAxMjE0NjhGNTRGQjcyMzE1ODdDNTNGNTgwMT48MzU2QjQwMTIxNDY4RjU0RkI3MjMxNTg3QzUzRjU4MDE+XSAvUHJldiAyODY5Mi9YUmVmU3RtIDI4MzgyPj4NCnN0YXJ0eHJlZg0KMjk0NDkNCiUlRU9G" }, log);
    private static async Task<ResponseInfo> SeFatura(ResponseInfo responseInfo, Parameters parametros, ILogger log)
    {
        log.LogInformation("Fatura *****");

        string formatoData = "MM/yyyy";

        var data = DateTime.TryParseExact(responseInfo.Data, formatoData, null, System.Globalization.DateTimeStyles.None, out DateTime dataValida);

        if (responseInfo.Fatura && data)
        {
            responseInfo.Mensagem = $"Ahh bacana! \u263A\nVi que você quer sua fatura do mês de {responseInfo.Data} 🧾!\nAcabamos de enviar a fatura em PDF para o seu e-mail 📧 ****@dominio.com.br.\n\nSe a fatura estiver em aberto vamos enviar o código por SMS 🗨.";
            responseInfo.IsEnviar = true;
        }

        if (responseInfo.Fatura && !data)
        {
            log.LogInformation("Listar Faturas");
            var listarFaturas = await ListaFaturas(log);
            var faturasAbertas = "";
            var faturasFechadas = "";

            if (listarFaturas.Count > 0)
            {
                await SendMessageWhatsapp(parametros.To, parametros.From, "Vou listar as faturas dos 12 últimos meses. Faturas em abertas e fechadas.", log);

                IFaturaStrategy abertasStrategy = new AbertasStrategy();
                faturasAbertas = abertasStrategy.ProcessFaturas(listarFaturas);
                await SendMessageWhatsapp(parametros.To, parametros.From, $"📈 *Em Aberto*\n{faturasAbertas}", log);
                log.LogInformation(faturasAbertas);

                IFaturaStrategy fechadasStrategy = new FechadasStrategy();
                faturasFechadas = fechadasStrategy.ProcessFaturas(listarFaturas);
                await SendMessageWhatsapp(parametros.To, parametros.From, $"📉 *Fechadas*\n{faturasFechadas}", log);
                log.LogInformation(faturasFechadas);

                responseInfo.Mensagem = $"Digite Fatura 'mes/ano' que você deseja para ver mais detalhes. Se preferir, digite 'minhas faturas' para eu listar o seu histórico de faturas.";
            }

            responseInfo.IsEnviar = true;
        }

        return responseInfo;
    }

    private static async Task<ResponseInfo> SeFaturaAberto(ResponseInfo responseInfo, Parameters parametros, ILogger log)
    {
        log.LogInformation("Fatura em aberto *****");

        string formatoData = "MM/yyyy";

        if (responseInfo.FaturaAberto)
        {
            log.LogInformation("Listar Faturas em aberto");
            var listarFaturas = await ListaFaturas(log);
            var faturasAbertas = "";

            if (listarFaturas.Count > 0)
            {
                IFaturaStrategy abertasStrategy = new AbertasStrategy();
                faturasAbertas = abertasStrategy.ProcessFaturas(listarFaturas);
                log.LogInformation(faturasAbertas);
            }

            responseInfo.Mensagem = $"📈 Suas faturas em aberto(s):\n\n{faturasAbertas}\nDigite Fatura 'mes/ano' que você deseja para ver mais detalhes. Se preferir, digite 'Minhas faturas' para eu listar o seu histórico de faturas.";
            responseInfo.IsEnviar = true;
        }

        return responseInfo;
    }

    private static async Task<List<Fatura>> ListaFaturas(ILogger log)
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                log.LogInformation("Listar Faturas Metodo");
                client.DefaultRequestHeaders.Add("cpf", "00170811140");
                client.DefaultRequestHeaders.Add("numero-conta", "12345678");

                var response = await client.GetAsync(Environment.GetEnvironmentVariable("URL_SERVICE_FATURA"));

                if (response.IsSuccessStatusCode)
                {
                    log.LogInformation("Tem registro");
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var responseFatura = JsonConvert.DeserializeObject<List<Fatura>>(jsonResponse);

                    return responseFatura;
                }
                else
                {
                    log.LogInformation("Nulo");
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex.Message);
            log.LogError(ex.StackTrace);
            return null;
            throw;
        }
    }

    private async static Task<bool> EnviarMensageria(Gerenciamento gerenciamento, ILogger log)
    {
        try
        {
            log.LogInformation("Gerenciamento *****");
            var client = new HttpClient();
            var response = await client.PostAsync("https://chwhatsappgptapp.azurewebsites.net/api/SendGerenciamento?code=oFx0pMLm7xAcA3O-yKO-WdQLktCSuidA6Xptxfc2BIeaAzFuG_V51A==", new StringContent(JsonConvert.SerializeObject(gerenciamento), Encoding.UTF8, "application/json"));
            if (response.StatusCode == HttpStatusCode.OK)
                return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex.Message);
        }
        return false;
    }

    private static string RemoverAcentos(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return texto;

        string normalizedString = texto.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();

        foreach (char c in normalizedString)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var textconvertido = sb.ToString().Normalize(NormalizationForm.FormC);

        return textconvertido;
    }
}