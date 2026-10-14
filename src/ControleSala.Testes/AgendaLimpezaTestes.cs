using ControleSala.Comum;
using Xunit;

namespace ControleSala.Testes;

public class AgendaLimpezaTestes
{
    // 2026-10-14 é uma quarta-feira (DayOfWeek.Wednesday = 3).
    static readonly LimpezaConfig Quarta18h = new() { Agendada = true, Dias = [3], Hora = "18:00" };
    static DateTime Em(string data) => DateTime.Parse(data, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Roda_no_horario_marcado_do_dia_certo()
    {
        Assert.True(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-14 18:00"), null, null));
    }

    [Fact]
    public void Nao_roda_antes_do_horario()
    {
        Assert.False(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-14 17:59"), null, null));
    }

    [Fact]
    public void Nao_roda_em_dia_que_nao_esta_na_lista()
    {
        // 2026-10-15 é quinta-feira (4), fora da lista [3].
        Assert.False(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-15 18:00"), null, null));
    }

    [Fact]
    public void Roda_dentro_da_janela_de_tolerancia_mas_nao_depois()
    {
        Assert.True(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-14 18:45"), null, null));
        Assert.False(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-14 19:30"), null, null));
    }

    [Fact]
    public void Nao_repete_se_ja_rodou_nesta_janela()
    {
        var jaRodou = Em("2026-10-14 18:05");
        Assert.False(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-14 18:10"), jaRodou, null));
    }

    [Fact]
    public void Roda_de_novo_na_semana_seguinte_mesmo_tendo_rodado_antes()
    {
        var semanaPassada = Em("2026-10-07 18:05"); // quarta anterior
        Assert.True(AgendaLimpeza.DeveRodar(Quarta18h, Em("2026-10-14 18:00"), semanaPassada, null));
    }

    [Fact]
    public void Adiamento_roda_quando_chega_a_hora_mesmo_fora_do_dia()
    {
        var agenda = new LimpezaConfig { Agendada = false }; // nem precisa estar agendada
        var adiadoPara = Em("2026-10-16 10:00");
        Assert.False(AgendaLimpeza.DeveRodar(agenda, Em("2026-10-16 09:59"), null, adiadoPara));
        Assert.True(AgendaLimpeza.DeveRodar(agenda, Em("2026-10-16 10:00"), null, adiadoPara));
    }

    [Fact]
    public void Adiamento_nao_repete_depois_de_rodar()
    {
        var adiadoPara = Em("2026-10-16 10:00");
        var rodouNoAdiamento = Em("2026-10-16 10:00");
        Assert.False(AgendaLimpeza.DeveRodar(new LimpezaConfig(), Em("2026-10-16 10:05"), rodouNoAdiamento, adiadoPara));
    }

    [Fact]
    public void Desligada_nunca_roda()
    {
        var desligada = new LimpezaConfig { Agendada = false, Dias = [3], Hora = "18:00" };
        Assert.False(AgendaLimpeza.DeveRodar(desligada, Em("2026-10-14 18:00"), null, null));
    }
}
