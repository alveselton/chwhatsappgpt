using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

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

public class Email
{
    public string Assunto { get; set; }
    public string Endereco { get; set; }
    public string Corpo { get; set; }
    public string ArquivoBase64 { get; set; }
}

public class Fatura
{
    private string codigoBarras;

    [JsonIgnore]
    [BsonId]
    public ObjectId Id { get; set; }
    public DateTime MesRef { get; set; } = default!;
    public string Cpf { get; set; } = default!;
    public string NumeroConta { get; set; } = default!;
    public string Pix { get; set; } = default!;
    public ValorMoeda PagamentoMinimo { get; set; } = default!;
    public ValorMoeda ValorFatura { get; set; } = default!;
    public ValorMoeda? ValorPago { get; set; } = default!;
    public ValorMoeda Creditos { get; set; } = default!;
    public string? CodigoBarras
    {
        get
        {
            if (ValorPago is not null)
                return null;

            return codigoBarras;
        }
        set
        {
            codigoBarras = value;
        }
    }
    public void AtualizarCodigoBarras()
    {
        if (ValorPago is not null)
        {
            CodigoBarras = null;
        }
    }
}

public class ValorMoeda
{
    public string Moeda { get; set; } = default!;
    public decimal Valor { get; set; } = default!;
}
