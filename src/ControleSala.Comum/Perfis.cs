using System.Text.Json;

namespace ControleSala.Comum;

public sealed class PerfilProfessor
{
    public string Nome { get; set; } = "";
    /// <summary>Versão das regras de uso que este professor aceitou (0 = ainda não aceitou).</summary>
    public int RegrasAceitas { get; set; }
    public DateTimeOffset? RegrasAceitasEm { get; set; }
}

/// <summary>Professores do PC do professor. Serve para saber quem está usando o programa (aviso ao aluno e registro de atividades).</summary>
public sealed class RepositorioPerfis
{
    readonly string _caminho;
    readonly TimeProvider _tempo;
    readonly List<PerfilProfessor> _perfis;

    public RepositorioPerfis(string? caminho = null, TimeProvider? tempo = null)
    {
        _caminho = caminho ?? Path.Combine(Pastas.Dados, "professores.json");
        _tempo = tempo ?? TimeProvider.System;
        _perfis = Ler(out bool tinhaPin);
        if (tinhaPin) ApagarPinAntigo();
    }

    public IReadOnlyList<PerfilProfessor> Perfis => _perfis;

    public PerfilProfessor Adicionar(string nome)
    {
        var perfil = new PerfilProfessor { Nome = NomeValido(nome) };
        _perfis.Add(perfil);
        Salvar();
        return perfil;
    }

    string NomeValido(string nome, PerfilProfessor? ignorar = null)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length == 0 || nome.Length > 60)
            throw new ArgumentException("Digite um nome com até 60 letras.");
        if (_perfis.Any(p => p != ignorar && string.Equals(p.Nome, nome, StringComparison.CurrentCultureIgnoreCase)))
            throw new ArgumentException("Já existe um professor com esse nome.");
        return nome;
    }

    /// <summary>Troca o nome (o perfil "Professor" de versões antigas ganha o nome de verdade).</summary>
    public void Renomear(PerfilProfessor perfil, string nome)
    {
        perfil.Nome = NomeValido(nome, perfil);
        Salvar();
    }

    public void AceitarRegras(PerfilProfessor perfil, int versao)
    {
        perfil.RegrasAceitas = versao;
        perfil.RegrasAceitasEm = _tempo.GetUtcNow();
        Salvar();
    }

    /// <summary>Remove o perfil. Não remove o último (o programa ficaria sem ninguém para entrar).</summary>
    public bool Remover(PerfilProfessor perfil)
    {
        if (_perfis.Count <= 1 || !_perfis.Remove(perfil))
            return false;
        Salvar();
        return true;
    }

    List<PerfilProfessor> Ler(out bool tinhaPin)
    {
        tinhaPin = false;
        try
        {
            if (File.Exists(_caminho))
            {
                string texto = File.ReadAllText(_caminho);
                tinhaPin = texto.Contains("\"Resumo\"");
                return JsonSerializer.Deserialize<List<PerfilProfessor>>(texto) ?? [];
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Arquivo ilegível: guarda uma cópia e começa do zero (o primeiro acesso recria os perfis).
            try { File.Move(_caminho, _caminho + ".corrompido", overwrite: true); } catch (IOException) { }
        }
        return [];
    }

    /// <summary>Versões antigas guardavam aqui o resumo do PIN: regrava o arquivo sem ele.</summary>
    void ApagarPinAntigo()
    {
        try { Salvar(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void Salvar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        string temporario = _caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(_perfis, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporario, _caminho, overwrite: true); // troca inteira: nunca fica meio escrito
    }
}
