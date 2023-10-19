using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace chwhatsappgpt.Interface;

public class FechadasStrategy : IFaturaStrategy
{
    public string ProcessFaturas(List<Fatura> faturas)
    {
        var faturasFechadas = "";
        var count = 1;

        foreach (var item in faturas.Where(x => x.ValorPago is not null))
        {
            faturasFechadas += $"*{count}.  {item.MesRef.ToString("MMMM/yyyy", new CultureInfo("pt-BR"))}*\n";
            faturasFechadas += $"💰 Valor: {item.ValorFatura.Moeda} {item.ValorFatura.Valor}\n\n";

            count++;
        }

        return faturasFechadas;
    }
}
