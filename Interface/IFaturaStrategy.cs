using System.Collections.Generic;

namespace chwhatsappgpt.Interface;

public interface IFaturaStrategy
{
    string ProcessFaturas(List<Fatura> faturas);
    string ProcessFatura(Fatura fatura);
}
