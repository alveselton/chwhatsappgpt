using Amazon.SecurityToken.Model.Internal.MarshallTransformations;
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
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static chwhatsappgpt.GetChat;
using static chwhatsappgpt.GetGpt;

namespace chwhatsappgpt;

public static class GetChat
{
    private static readonly Lazy<MongoClient> lazyClient = new(InitializeMongoClient);
    private static readonly MongoClient client = lazyClient.Value;

    private static readonly string apiKey = "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";
    private static readonly string apiUrl = "https://api.openai.com/v1/engines/text-davinci-003/completions";
    private static readonly string endpoint = "https://api.openai.com/v1/chat/completions";

    [FunctionName("GetChat")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("Inicio Chat");

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

        log.LogInformation(requestBody);


        var parameters = parametrosPesquisa(requestBody, log);

        var content = $"No \"Texto\" tem alguma data? alguma informacao se é sobre fatura? Tem pedido de informacao sobre produto? Tem pedido de cancelamento? Tem informacao de roubo ou perda de cartao? Tem algum pedido de informacao de Cartao? Tem pedido de informacao de Cadastro? Informacao sobre nao reconhecer Compras? O texto contem saudacao inicial de conversa?\nObservacao: \n- Data precisa que seja formatada em MM/YYYY\n- Se tiver no texto \"fatura do meu cartao\" a fatura= sim e cartao = nao;\n- Se tiver no texto \"saldo cartao\" a fatura = nao e cartao = sim e Saldo-Cartao = sim;\n- Se tiver no texto \"saldo fatura\" a fatura = sim e cartao = nao e Saldo-Cartao = nao;\n- Nao pode haver acentuacao;\n\nPreciso que o dado sejam resumido. \n- Data: <<valor>>\n- Fatura: Sim ou Nao\n- Produto: Sim ou Nao\n- Roubo ou Perda de Cartao: Sim ou Nao\n- Cancelamento: Sim ou Nao\n- Cartao: Sim ou Nao\n- Cadastro: Sim ou Nao\n- Nao-Reconhece-Compras: Sim ou Nao\n- Saudacao: Sim ou Nao\n- Saldo-Cartao: Sim ou Nao\n\nTexto: \"{parameters.Question}\"";
        var response = await GenerateResponseGptTurbo(content);

        if (response.choices.Count > 0)
        {
            log.LogInformation("Fluxo " + response.choices[0].message.content);

            var responseTratado = ConverterValor(RemoverAcentos(response.choices[0].message.content), log);
            log.LogInformation("response - " + Newtonsoft.Json.JsonConvert.SerializeObject(responseTratado));

            //SAUDACAO
            var saudacao = SeSaudacao(responseTratado);
            if (saudacao.Saudacao && saudacao.IsEnviar)
            {
                log.LogInformation("Saudacao " + saudacao.ToString());
                return new OkObjectResult(saudacao.Mensagem);
            }

            //FATURA
            var fatura = SeFatura(responseTratado);
            if (fatura.Fatura && saudacao.IsEnviar)
            {
                log.LogInformation("Fatura " + fatura.ToString());
                return new OkObjectResult(fatura.Mensagem);
            }

            log.LogInformation("Final");
            return new OkObjectResult(responseTratado);
        }
        else
        {
            return new BadRequestObjectResult("Erro na chamada à API do OpenAI.");
        }
    }

    private static async Task<OpenAIResponse> GenerateResponse(string question)
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

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

    private static async Task<OpenAIResponse> GenerateResponseGptTurbo(string question)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var parameters = new
        {
            model = "gpt-3.5-turbo",
            messages = new[] { new { role = "user", content = question }, },
            max_tokens = 500,
            temperature = 0.57f,
        };

        var response = await client.PostAsync(endpoint, new StringContent(JsonConvert.SerializeObject(parameters), Encoding.UTF8, "application/json"));// new StringContent(json));

        // Read the response
        var responseContent = await response.Content.ReadAsStringAsync();
 
        var responseObject = JsonConvert.DeserializeObject<OpenAIResponse>(responseContent);
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
                        responseInfo.Data = value;
                        break;
                    case "- Fatura":
                        responseInfo.Fatura = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Produto":
                        responseInfo.Produto = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Roubo-Perda-Cartao":
                        responseInfo.RouboPerdaCartao = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Cancelamento":
                        responseInfo.Cancelamento = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Cartao":
                        responseInfo.Cartao = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Cadastro":
                        responseInfo.Cadastro = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Nao-Reconhece-Compras":
                        responseInfo.NaoReconheceCompras = value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "- Saudacao":
                        responseInfo.Saudacao= value.Equals("Sim", StringComparison.OrdinalIgnoreCase);
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

            if (requestBody.ContainsKey("profileName"))
                obj.ProfileName = requestBody["profileName"];

            if (requestBody.ContainsKey("waId"))
                obj.WaId = requestBody["waId"];

            if (requestBody.ContainsKey("question"))
                obj.Question = requestBody["question"];

            if (requestBody.ContainsKey("body"))
                obj.Body = requestBody["body"];

            if (requestBody.ContainsKey("fromreceived"))
                obj.Fromreceived = requestBody["fromreceived"];

            if (requestBody.ContainsKey("to"))
                obj.To = requestBody["to"];

            obj.Parametros = requestBody;
            obj.User = obj?.Fromreceived?.Split(":")[1];
            log.LogInformation("parametrosPesquisa - " + obj.ToString());
            return obj;
        }
        else
        {
            var obj = JsonConvert.DeserializeObject<Parameters>(parameters);
            log.LogInformation("parametrosPesquisa - " + obj.ToString());
            return obj;
        }
    }

    //SAUDACAO
    private static ResponseInfo SeSaudacao(ResponseInfo responseInfo)
    {
        if (responseInfo.Saudacao &&
            !new[] { responseInfo.Fatura, responseInfo.Produto, responseInfo.RouboPerdaCartao, responseInfo.Cancelamento, responseInfo.Cartao, responseInfo.Cadastro, responseInfo.NaoReconheceCompras }.Any(field => field))
        {
            responseInfo.Mensagem = "Olá, {nome}! Tudo bem?\n Para que possamos ajuda-lo você pode solicitar o que desejar em poucas palavras.\n Exemplo:\n- Minha fatura de Janeiro de 2023;\n- Saldo do meu cartão;\n- Quais os produtos que posso adquirir;\n- Cancelar cartão;\n- Nao Reconheço uma compra;\n- Bloquear meu cartão;\n\nAhhh.. Você pode enviar áudio também. Clique [aqui]({whatsappLink}) para entrar em contato pelo WhatsApp.";
            responseInfo.IsEnviar = true;

            return responseInfo;
        }

        return responseInfo;
    }

    //FATURA
    private static ResponseInfo SeFatura(ResponseInfo responseInfo)
    {
        string formatoData = "MM/yyyy";

        if (responseInfo.Fatura && DateTime.TryParseExact(responseInfo.Data, formatoData, null, System.Globalization.DateTimeStyles.None, out DateTime dataValida)) 
        {
            responseInfo.Mensagem = $"Ahh bacana! Vi que você quer sua fatura do mês de {responseInfo.Data}!\nAcabamos de enviar a fatura em PDF para o seu e-mail ****@dominio.com.br.\nSe a fatura estiver em aberto vamos enviar o código por SMS também.";
            responseInfo.IsEnviar = true;
        }

        return responseInfo;
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

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public class RequestBody
    {
        public string texto {get; set;}
    }

    public class ChatCompletionResponse
    {
        public string Id { get; set; }
        public string Object { get; set; }
        public long Created { get; set; }
        public string Model { get; set; }
        public List<Choice> Choices { get; set; }
        public Usage Usage { get; set; }
    }

    public class Choice
    {
        public int Index { get; set; }
        public Message Message { get; set; }
        public string FinishReason { get; set; }
    }

    public class Message
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }

    public class Usage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    public class Parameters
    {
        public string ProfileName { get; set; }
        public string WaId { get; set; }
        public string Body { get; set; }
        public string Fromreceived { get; set; }
        public string To { get; set; }
        public string Question { get; set; }
        public string User { get; set; }
        public string NumMedia { get; set; }
        public IEnumerable<string> MediaUrl { get; set; }

        public Dictionary<string, string> Parametros { get; set; }
    }

    public class ResponseInfo
    {
        public string Data { get; set; }
        public bool Fatura { get; set; }
        public bool Produto { get; set; }
        public bool RouboPerdaCartao { get; set; }
        public bool Cancelamento { get; set; }
        public bool Cartao { get; set; }
        public bool Cadastro { get; set; }
        public bool Saudacao { get; set; }
        public bool NaoReconheceCompras { get; set; }
        public string Mensagem { get; set; }
        public bool IsEnviar { get; set; }
    }
}