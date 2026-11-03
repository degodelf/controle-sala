using System.Text.Json;

namespace ControleSala.Comum;

public sealed class ComputadorSala
{
    public string Nome { get; set; } = "";
    public string Endereco { get; set; } = "";
    public int Porta { get; set; } = Protocolo.PortaPadrao;
    /// <summary>Endereço de rede (MAC) da placa, descoberto quando o PC fica online; usado para ligá-lo (Wake-on-LAN).</summary>
    public string? Mac { get; set; }
}

/// <summary>Os computadores da sala, na ordem em que aparecem na grade. Arquivo: <c>sala.json</c>.</summary>
public sealed class RepositorioSala
{
    readonly string _caminho;
    readonly List<ComputadorSala> _computadores;

    public RepositorioSala(string? caminho = null)
    {
        _caminho = caminho ?? Path.Combine(Pastas.Dados, "sala.json");
        _computadores = Ler();
    }

    public IReadOnlyList<ComputadorSala> Computadores => _computadores;

    /// <summary>Mensagem de erro para mostrar ao professor, ou null se os dados servem.</summary>
    public string? Validar(string nome, string endereco, int porta, ComputadorSala? ignorar = null)
    {
        nome = (nome ?? "").Trim();
        endereco = (endereco ?? "").Trim();
        if (nome.Length == 0 || nome.Length > 40) return "Digite um nome com até 40 letras (por exemplo, \"Mesa 05\").";
        if (endereco.Length == 0 || endereco.Length > 255 || Uri.CheckHostName(endereco) == UriHostNameType.Unknown)
            return "Digite o endereço IP (por exemplo, 192.168.0.15) ou o nome do computador.";
        if (porta is < 1 or > 65535) return "A porta deve estar entre 1 e 65535.";
        if (_computadores.Any(c => c != ignorar && c.Porta == porta
                && string.Equals(c.Endereco, endereco, StringComparison.OrdinalIgnoreCase)))
            return "Esse computador já está na sala.";
        return null;
    }

    public ComputadorSala Adicionar(string nome, string endereco, int porta = Protocolo.PortaPadrao)
    {
        if (Validar(nome, endereco, porta) is { } erro) throw new ArgumentException(erro);
        var c = new ComputadorSala { Nome = nome.Trim(), Endereco = endereco.Trim(), Porta = porta };
        _computadores.Add(c);
        Salvar();
        return c;
    }

    /// <summary>Troca os dados por um objeto novo (quem usa a lista percebe que o computador mudou). Mantém a posição.</summary>
    public ComputadorSala Atualizar(ComputadorSala atual, string nome, string endereco, int porta)
    {
        int i = _computadores.IndexOf(atual);
        if (i < 0) throw new ArgumentException("Esse computador não está mais na sala.");
        if (Validar(nome, endereco, porta, atual) is { } erro) throw new ArgumentException(erro);
        var novo = new ComputadorSala { Nome = nome.Trim(), Endereco = endereco.Trim(), Porta = porta };
        _computadores[i] = novo;
        Salvar();
        return novo;
    }

    public bool Remover(ComputadorSala c)
    {
        if (!_computadores.Remove(c)) return false;
        Salvar();
        return true;
    }

    /// <summary>Anda <paramref name="delta"/> posições (negativo = para cima). Retorna false se já está na ponta.</summary>
    /// <summary>Guarda o MAC descoberto de um computador (para ligá-lo depois). Só grava se mudou.</summary>
    public void DefinirMac(ComputadorSala c, string? mac)
    {
        if (mac == null || c.Mac == mac || !_computadores.Contains(c)) return;
        c.Mac = mac;
        Salvar();
    }

    /// <summary>Coloca os computadores em ordem pelo nome (PC 01, PC 02... PC 10 na ordem certa).</summary>
    public void OrdenarPorNome()
    {
        _computadores.Sort((a, b) => CompararNatural(a.Nome, b.Nome));
        Salvar();
    }

    /// <summary>Compara nomes tratando números embutidos como número (PC 2 antes de PC 10).</summary>
    static int CompararNatural(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int ia = i, jb = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a.AsSpan(ia, i - ia).TrimStart('0');
                var nb = b.AsSpan(jb, j - jb).TrimStart('0');
                if (na.Length != nb.Length) return na.Length - nb.Length;
                int c = na.SequenceCompareTo(nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (a.Length - i) - (b.Length - j);
    }

    /// <summary>Renomeia todos na ordem da lista como "PC 01", "PC 02"... Ajuda a identificar cada máquina na grade.</summary>
    public void Numerar(string prefixo = "PC")
    {
        for (int i = 0; i < _computadores.Count; i++)
            _computadores[i].Nome = $"{prefixo} {i + 1:00}";
        Salvar();
    }

    public bool Mover(ComputadorSala c, int delta)
    {
        int de = _computadores.IndexOf(c), para = de + delta;
        if (de < 0 || para < 0 || para >= _computadores.Count) return false;
        _computadores.RemoveAt(de);
        _computadores.Insert(para, c);
        Salvar();
        return true;
    }

    List<ComputadorSala> Ler()
    {
        try
        {
            if (File.Exists(_caminho))
                return JsonSerializer.Deserialize<List<ComputadorSala>>(File.ReadAllText(_caminho)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            try { File.Move(_caminho, _caminho + ".corrompido", overwrite: true); } catch (IOException) { }
        }
        return [];
    }

    void Salvar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        string temporario = _caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(_computadores, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporario, _caminho, overwrite: true);
    }
}
