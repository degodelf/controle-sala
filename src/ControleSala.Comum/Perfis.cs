using System.Security.Cryptography;
using System.Text.Json;

namespace ControleSala.Comum;

public sealed class PerfilProfessor
{
    public string Nome { get; set; } = "";
    /// <summary>Sal aleatório (Base64). O PIN nunca é guardado, só o resumo PBKDF2.</summary>
    public string Sal { get; set; } = "";
    public string Resumo { get; set; } = "";
    public int Iteracoes { get; set; }
    public int Falhas { get; set; }
    public DateTimeOffset? BloqueadoAte { get; set; }
}

public enum ResultadoPin { Correto, Incorreto, Bloqueado }

/// <summary>
/// Perfis de professor (nome + PIN) do PC do professor. Serve para saber quem está usando o programa
/// (aviso ao aluno, histórico) e para um aluno que sente ali sem ninguém por perto não conseguir mexer na sala.
/// O PIN protege contra uso casual; quem tem administrador no PC do professor pode apagar o arquivo.
/// </summary>
public sealed class RepositorioPerfis
{
    public const int FalhasAteBloquear = 5;
    public static readonly TimeSpan BloqueioInicial = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan BloqueioMaximo = TimeSpan.FromMinutes(5);

    readonly string _caminho;
    readonly int _iteracoes;
    readonly TimeProvider _tempo;
    readonly List<PerfilProfessor> _perfis;

    public RepositorioPerfis(string? caminho = null, int iteracoes = 600_000, TimeProvider? tempo = null)
    {
        _caminho = caminho ?? Path.Combine(Pastas.Dados, "professores.json");
        _iteracoes = iteracoes;
        _tempo = tempo ?? TimeProvider.System;
        _perfis = Ler();
    }

    public IReadOnlyList<PerfilProfessor> Perfis => _perfis;

    public static bool PinValido(string? pin) =>
        pin is { Length: >= 4 and <= 8 } && pin.All(char.IsAsciiDigit);

    public PerfilProfessor Adicionar(string nome, string pin)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length == 0 || nome.Length > 60)
            throw new ArgumentException("Digite um nome com até 60 letras.");
        if (!PinValido(pin))
            throw new ArgumentException("O PIN deve ter de 4 a 8 números.");
        if (_perfis.Any(p => string.Equals(p.Nome, nome, StringComparison.CurrentCultureIgnoreCase)))
            throw new ArgumentException("Já existe um professor com esse nome.");

        var perfil = new PerfilProfessor { Nome = nome };
        DefinirPin(perfil, pin);
        _perfis.Add(perfil);
        Salvar();
        return perfil;
    }

    public ResultadoPin Verificar(PerfilProfessor perfil, string pin, out TimeSpan espera)
    {
        var agora = _tempo.GetUtcNow();
        if (perfil.BloqueadoAte is { } ate && ate > agora)
        {
            espera = ate - agora;
            return ResultadoPin.Bloqueado;
        }
        espera = TimeSpan.Zero;

        if (PinConfere(perfil, pin))
        {
            if (perfil.Falhas != 0 || perfil.BloqueadoAte != null)
            {
                perfil.Falhas = 0;
                perfil.BloqueadoAte = null;
                Salvar();
            }
            return ResultadoPin.Correto;
        }

        perfil.Falhas++;
        if (perfil.Falhas >= FalhasAteBloquear)
        {
            // 30 s, 60 s, 120 s... até 5 min, a cada nova falha depois da quinta.
            int dobras = Math.Min(perfil.Falhas - FalhasAteBloquear, 10);
            var bloqueio = TimeSpan.FromTicks(Math.Min(BloqueioInicial.Ticks << dobras, BloqueioMaximo.Ticks));
            perfil.BloqueadoAte = agora + bloqueio;
            espera = bloqueio;
        }
        Salvar();
        return perfil.BloqueadoAte > agora ? ResultadoPin.Bloqueado : ResultadoPin.Incorreto;
    }

    /// <summary>Troca o PIN se o atual estiver certo. Lança ArgumentException se o PIN novo for inválido.</summary>
    public ResultadoPin TrocarPin(PerfilProfessor perfil, string pinAtual, string pinNovo, out TimeSpan espera)
    {
        if (!PinValido(pinNovo))
            throw new ArgumentException("O PIN deve ter de 4 a 8 números.");
        var resultado = Verificar(perfil, pinAtual, out espera);
        if (resultado == ResultadoPin.Correto)
        {
            DefinirPin(perfil, pinNovo);
            Salvar();
        }
        return resultado;
    }

    /// <summary>Remove o perfil. Não remove o último (o programa ficaria sem ninguém para entrar).</summary>
    public bool Remover(PerfilProfessor perfil)
    {
        if (_perfis.Count <= 1 || !_perfis.Remove(perfil))
            return false;
        Salvar();
        return true;
    }

    void DefinirPin(PerfilProfessor perfil, string pin)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        perfil.Sal = Convert.ToBase64String(sal);
        perfil.Iteracoes = _iteracoes;
        perfil.Resumo = Convert.ToBase64String(Calcular(pin, sal, _iteracoes));
        perfil.Falhas = 0;
        perfil.BloqueadoAte = null;
    }

    static bool PinConfere(PerfilProfessor perfil, string pin)
    {
        try
        {
            var esperado = Convert.FromBase64String(perfil.Resumo);
            var obtido = Calcular(pin, Convert.FromBase64String(perfil.Sal), perfil.Iteracoes);
            return CryptographicOperations.FixedTimeEquals(esperado, obtido);
        }
        catch (FormatException)
        {
            return false; // arquivo adulterado: ninguém entra com este perfil
        }
    }

    static byte[] Calcular(string pin, byte[] sal, int iteracoes) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, sal, Math.Max(iteracoes, 1), HashAlgorithmName.SHA256, 32);

    List<PerfilProfessor> Ler()
    {
        try
        {
            if (File.Exists(_caminho))
                return JsonSerializer.Deserialize<List<PerfilProfessor>>(File.ReadAllText(_caminho)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Arquivo ilegível: guarda uma cópia e começa do zero (o primeiro acesso recria os perfis).
            try { File.Move(_caminho, _caminho + ".corrompido", overwrite: true); } catch (IOException) { }
        }
        return [];
    }

    void Salvar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        string temporario = _caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(_perfis, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporario, _caminho, overwrite: true); // troca inteira: nunca fica meio escrito
    }
}
