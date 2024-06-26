using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Twilio.TwiML;
using static chwhatsappgpt.GetChat;

namespace chwhatsappgpt;

public static class GetGpt
{
    private static readonly Lazy<MongoClient> lazyClient = new(InitializeMongoClient);
    private static readonly MongoClient client = lazyClient.Value;

    private static readonly string apiKey = "";
    private static readonly string apiUrl = "https://api.openai.com/v1/engines/text-davinci-003/completions";

    [FunctionName("GetGpt")]
    public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("Inicio Requisicao");

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        log.LogInformation(requestBody);

        string profileName = "";
        string waId = "";
        string body = "";
        string fromreceived = "";
        string to = "";
        string question = "";

        var parameters = requestBody.Split('&')
            .Select(param => param.Split('='))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

        var messageResponse = new MessagingResponse();

        if (parameters.TryGetValue("ProfileName", out profileName) &&
            parameters.TryGetValue("WaId", out waId) &&
            parameters.TryGetValue("Body", out body) &&
            parameters.TryGetValue("From", out fromreceived) &&
            parameters.TryGetValue("To", out to))
        {
            log.LogInformation($"ProfileName: {profileName}");
            log.LogInformation($"WaId: {waId}");
            log.LogInformation($"Body: {body}");
            log.LogInformation($"From: {fromreceived}");
            log.LogInformation($"To: {to}");
        }

        var user = fromreceived.Split(":")[1];

        if (!VerificaSaldo(user, log))
        {
            log.LogInformation($"Saldo insuficiente {user}");
            return new OkObjectResult("Saldo insuficiente para uma nova pergunta. Recarregue agora mesmo e continue gerando resultados através da inteligência artificial mais eficiente.");
        }

        Audio audio;
        SeExistirAudio(log, body, out question, parameters, out audio);

        var response = await GenerateResponse(question);

        var successResponse = new
        {
            success = true,
            message = response?.choices[0]?.text
        };

        if (successResponse.success)
        {
            var itemHistorico = new Historico(user, question, response, audio.IsExist);

            IMongoCollection<Historico> historico = client.GetDatabase("chatgpt").GetCollection<Historico>("historico");
            historico.InsertOne(itemHistorico);
        }

        log.LogInformation($"Saldo em conta: {SaldoConta(user)}");

        // Converte o objeto em JSON e retorna como Ok
        return new OkObjectResult(successResponse.message);
    }

    public static void parametrosPesquisa(string parameters)
    {
        var requestBody = parameters.Split('&')
            .Select(param => param.Split('='))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));


    }

    public static void SeExistirAudio(ILogger log, string body, out string question, Dictionary<string, string> parameters, out Audio audio)
    {
        audio = ContainsAudio(parameters, log);
        if (audio.IsExist is true)
            question = TranscreverAudio(audio.Url, log);
        else
            question = body;
    }

    public static string TranscreverAudio(string audioUrl, ILogger log)
    {
        try
        {
            string credencial = @"{
                                'type': 'service_account',
                                'project_id': 'cc-domain-prod',
                                'private_key_id': '',
                                'private_key': '',
                                'client_email': 'service-speech-to-text@cc-domain-prod.iam.gserviceaccount.com',
                                'client_id': '',
                                'auth_uri': 'https://accounts.google.com/o/oauth2/auth',
                                'token_uri': 'https://oauth2.googleapis.com/token',
                                'auth_provider_x509_cert_url': 'https://www.googleapis.com/oauth2/v1/certs',
                                'client_x509_cert_url': 'https://www.googleapis.com/robot/v1/metadata/x509/service-speech-to-text%40cc-domain-prod.iam.gserviceaccount.com',
                                'universe_domain': 'googleapis.com'
                                }";

            GoogleCredential credentials = GoogleCredential.FromJson(credencial);

            SpeechClientBuilder builder = new SpeechClientBuilder();
            builder.Credential = credentials;

            var clientGoogle = builder.Build();

            RecognitionAudio audio = RecognitionAudio.FetchFromUri(audioUrl);

            // Configure as opções de reconhecimento
            var config = new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.OggOpus,
                SampleRateHertz = 16000,
                LanguageCode = "pt-BR" // Idioma do áudio
            };

            RecognizeResponse response = clientGoogle.Recognize(config, audio);

            var texto = response.Results[0].Alternatives[0].Transcript;
            log.LogInformation(texto);

            return texto;
        }
        catch (Exception ex)
        {
            log.LogError(ex.Message);
        }

        return "";
    }

    private static Audio ContainsAudio(Dictionary<string, string> parameters, ILogger log)
    {
        var audio = new Audio();

        if (parameters.TryGetValue("NumMedia", out var numMedia) && int.TryParse(numMedia, out var mediaCount))
        {
            if (mediaCount > 0)
            {
                log.LogInformation("Contém áudio");

                for (int i = 0; i < mediaCount; i++)
                {
                    var mediaUrl = parameters.TryGetValue($"MediaUrl{i}", out var mediaUrlValue)
                        ? mediaUrlValue
                        : string.Empty;

                    if (!string.IsNullOrEmpty(mediaUrl))
                    {
                        audio.Url = mediaUrl;
                        audio.IsExist = true;
                        break;
                    }

                    log.LogInformation($"URL do áudio {i}: {mediaUrl}");
                }
            }
        }

        return audio;
    }

    private static bool VerificaSaldo(string user, ILogger log)
    {
        var saldoConta = SaldoConta(user);
        var saldoPesquisa = SaldoPesquisa(user);
        log.LogInformation($"Saldo em conta: {saldoConta} - Saldo Pesquisado: {saldoPesquisa} - User: {user}");

        return saldoConta > saldoPesquisa;
    }

    public static decimal SaldoConta(string user)
    {
        IMongoCollection<Billing> historico = client.GetDatabase("chatgpt").GetCollection<Billing>("billing");

        var filtro = Builders<Billing>.Filter.Eq(x => x.User, user);

        var aggregate = historico.Aggregate()
        .Match(filtro)
        .Group(new BsonDocument
        {
            { "_id", BsonNull.Value }, // Agrupamento sem chave
            { "total", new BsonDocument("$sum", "$credit") } // Soma dos créditos
        });

        var resultado = aggregate.FirstOrDefault();
        return resultado?.GetValue("total").ToDecimal() ?? 0;
    }

    public static decimal SaldoPesquisa(string user)
    {
        IMongoCollection<Historico> historico = client.GetDatabase("chatgpt").GetCollection<Historico>("historico");

        var filtro = Builders<Historico>.Filter.Eq(x => x.User, user);

        var aggregate = historico.Aggregate()
        .Match(filtro)
        .Group(new BsonDocument
        {
            { "_id", BsonNull.Value }, // Agrupamento sem chave
            { "total", new BsonDocument("$sum", "$price") } // Soma dos créditos
        });

        var resultado = aggregate.FirstOrDefault();

        return resultado?.GetValue("total").ToDecimal() ?? 0;
    }

    public static MongoClient InitializeMongoClient()
    {
        return new MongoClient(Environment.GetEnvironmentVariable("MONGODB_ATLAS_URI"));
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
                    max_tokens = 3000
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

    public class OpenAIResponse
    {
        public string warning { get; set; }
        public string id { get; set; }
        public string @object { get; set; }
        public long created { get; set; }
        public string model { get; set; }
        public List<Choice> choices { get; set; }
        public Usage usage { get; set; }
    }

    public class Choice
    {
        public string text { get; set; }
        public int index { get; set; }
        public object logprobs { get; set; }
        public string finish_reason { get; set; }
        public Message message { get; set; }
    }

    public class Message
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    public class Usage
    {
        public int prompt_tokens { get; set; }
        public int completion_tokens { get; set; }
        public int total_tokens { get; set; }
    }

    public class Billing
    {
        [BsonElement("to")]
        public string To { get; set; } = default!;
        
        [BsonElement("user")]
        public string User { get; set; } = default!;

        [BsonElement("data_evento")]
        public DateTime DataEvento { get; set; } = default!;

        [BsonElement("credit")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Credit { get;set; } = 0;

        [BsonElement("observacao")]
        public string Observacao { get; set; } = default!;
    }

    public class Historico
    {
        public Historico(string user, string question, OpenAIResponse response, bool isAudio) 
        { 
            User = user;
            DataRequisicao = DateTime.Now;
            Price = isAudio ? 1.5M : 1M;
            Question = question;
            Response = response;
        }

        [BsonElement("user")]
        public string User { get; set; } = default!;

        [BsonElement("data_requisicao")]
        public DateTime DataRequisicao { get; set; } = default!;

        [BsonElement("price")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; } = 0;

        [BsonElement("question")]
        public string Question { get; set; } = default;

        [BsonElement("response")]
        public OpenAIResponse Response { get; set; } = default!;
    }

    public class Audio
    {
        public bool IsExist { get; set; } = false;
        public string Url { get; set; } = default!;       
    }
}
