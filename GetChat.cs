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

        var response = await client.PostAsync(endpoint,
            new StringContent(JsonConvert.SerializeObject(parameters),
            Encoding.UTF8,
            "application/json"));

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseObject = JsonConvert.DeserializeObject<OpenAIResponse>(responseContent);

        log.LogInformation("Request Gpt - " + Newtonsoft.Json.JsonConvert.SerializeObject(parameters));
        log.LogInformation("GPT - " + Newtonsoft.Json.JsonConvert.SerializeObject(responseObject));

        return responseObject;
    }

    private static ResponseInfo ConverterValor(string response, ILogger log)
    {
        var responseText = response;
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