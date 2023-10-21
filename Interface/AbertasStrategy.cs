using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace chwhatsappgpt.Interface;
public class AbertasStrategy : IFaturaStrategy
{
    public string ProcessFatura(Fatura fatura)
    {
        throw new NotImplementedException();
    }

    public string ProcessFaturas(List<Fatura> faturas)
    {
        var faturasAbertas = "";
        var count = 1;

        foreach (var item in faturas.Where(x => x.ValorPago is null))
        {
            faturasAbertas += $"{count}.  {item.MesRef.ToString("MMMM/yyyy", new CultureInfo("pt-BR")).ToUpper()}\n";
            faturasAbertas += $"💰 Valor: {item.ValorFatura.Valor.ToString("C", new CultureInfo("pt-BR"))}\n";
            faturasAbertas += $"🗓 Vencimento: {item.MesRef.ToString("dd/MM/yyyy")}\n";

            if (item.MesRef < DateTime.Now)
                faturasAbertas += $"🔴 Fatura Vencida\n";
            
            faturasAbertas += $"\n";
            
            count++;
        }

        return faturasAbertas;
    }
}
