using System.Collections.Generic;

namespace chwhatsappgpt;

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

public class RequestBody
{
    public string texto { get; set; }
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

public class Gerenciamento
{
    public string Canal { get; set; }
    public string Email { get; set; }
    public string Sms { get; set; }
    public string Celular { get; set; }
    public string Mensagem { get; set; }
    public string ArquivoBase64 { get; set; }
}