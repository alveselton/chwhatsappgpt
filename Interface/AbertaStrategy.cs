using System;
using System.Collections.Generic;
using System.Globalization;

namespace chwhatsappgpt.Interface;
public class AbertaStrategy : IFaturaStrategy
{    
    public string ProcessFatura(Fatura fatura)
    {
        var codigoBarras = "00190.50095 40144.816069 06809.350314 3 37370000000100";
        var pix = "{1B59867C-097D-484D-A5DC-371DD6E15ED9}";

        var faturasAbertas = "";
        var count = 1;

        if (fatura.ValorPago is null)
        {
            faturasAbertas += $"{count}.  {fatura.MesRef.ToString("MMMM/yyyy", new CultureInfo("pt-BR")).ToUpper()}\n";
            faturasAbertas += $"💰 Valor: {fatura.ValorFatura.Valor.ToString("C", new CultureInfo("pt-BR"))}\n";
            faturasAbertas += $"🗓 Vencimento: {fatura.MesRef.ToString("dd/MM/yyyy")}\n";

            if (fatura.MesRef < DateTime.Now)
            {
                faturasAbertas += $"Código de Barras:\n\n{codigoBarras}\n";
                faturasAbertas += $"Pix:\n\n{pix}";
            }

            faturasAbertas += $"\n";
        }
        
        return faturasAbertas;
    }

    public string ProcessFaturas(List<Fatura> faturas)
    {
        throw new NotImplementedException();
    }
}
