using System.Text.Json;

namespace ControleSala.Comum;

public sealed class ComputadorSala
{
    public string Nome { get; set; } = "";
    public string Endereco { get; set; } = "";
    public int Porta { get; set; } = Protocolo.PortaPadrao;
    /// <summary>Endereço de rede (MAC) da placa, descoberto quando o PC fica online; usado para ligá-lo (Wake-on-LAN).</summary>
    public string? Mac { get; set; }
    /// <summary>Último nome escolhido na instalação do PC que já foi aplicado aqui (ver <see cref="RepositorioSala.AplicarNomeEscolhido"/>).</summary>
    public string? NomeEscolhido { get; set; }
    /// <summary>Nome que o próprio PC anuncia na rede. Não muda quando o professor renomeia: é assim que o PC é reconhecido se o IP mudar.</summary>
    public string? NomeNaRede { get; set; }
}

/// <summary>Os computadores da sala, na ordem em que aparecem na grade. Arquivo: <c>sala.json</c>.</summary>
public sealed class RepositorioSala
{
    readonly string _caminho;
    readonly List<ComputadorSala> _computadores;
    readonly HashSet<string> _removidos;

    public RepositorioSala(string? caminho = null)
    {
        _caminho = caminho ?? Path.Combine(Pastas.Dados, "sala.json");
        _computadores = Ler();
        _removidos = LerRemovidos();
    }

    /// <summary>Quem o professor tirou da lista não volta sozinho: só por "Procurar na rede" ou adicionando à mão.</summary>
    string CaminhoRemovidos => Path.ChangeExtension(_caminho, ".removidos.json");

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
        if (_removidos.Remove(NomeComparavel(c.Nome))) SalvarRemovidos();
        return c;
    }

    /// <summary>
    /// Um PC que provou ser desta sala apareceu na rede: entra na lista sozinho, no lugar certo pelo nome, sem mexer na ordem
    /// dos outros. Null se ele já está na lista (pelo endereço ou pelo nome), se o professor o removeu ou se o nome não serve.
    /// </summary>
    public ComputadorSala? AdicionarDaRede(AgenteEncontrado achado)
    {
        if (!PodeEntrarDaRede(achado)) return null;

        var c = new ComputadorSala { Nome = achado.Nome.Trim(), Endereco = achado.Endereco, Porta = achado.Porta, NomeNaRede = achado.Nome.Trim() };
        int lugar = _computadores.FindIndex(outro => CompararNatural(outro.Nome, c.Nome) > 0);
        _computadores.Insert(lugar < 0 ? _computadores.Count : lugar, c);
        Salvar();
        return c;
    }

    /// <summary>O PC anunciado ainda não está na lista e pode entrar sozinho?</summary>
    public bool PodeEntrarDaRede(AgenteEncontrado achado)
    {
        string nome = NomeComparavel(achado.Nome);
        return nome.Length > 0 && !_removidos.Contains(nome) && !TemNome(nome) && Validar(achado.Nome, achado.Endereco, achado.Porta) == null;
    }

    /// <summary>True se o PC anunciado é outro computador com o nome de um que já está na lista: ele não entra sozinho.</summary>
    public bool NomeRepetido(AgenteEncontrado achado) => TemNome(NomeComparavel(achado.Nome)) && NoLugar(achado) == null;

    /// <summary>O computador da lista que está no endereço anunciado.</summary>
    ComputadorSala? NoLugar(AgenteEncontrado achado) => _computadores.Find(c => c.Porta == achado.Porta
        && string.Equals(c.Endereco, achado.Endereco, StringComparison.OrdinalIgnoreCase));

    bool TemNome(string nome) => _computadores.Any(c => NomeComparavel(c.Nome) == nome || Identidade(c) == nome
        || (c.NomeEscolhido is { } escolhido && NomeComparavel(escolhido) == nome));

    /// <summary>Como o PC se apresenta na rede. O nome da lista só vale enquanto ele ainda não se anunciou.</summary>
    static string Identidade(ComputadorSala c) => NomeComparavel(c.NomeNaRede ?? c.NomeEscolhido ?? c.Nome);

    /// <summary>
    /// O anúncio mostra um PC da lista em outro endereço? Quem responde sim não muda nada: antes de chamar <see cref="Reorganizar"/>,
    /// confira o anúncio com <see cref="ConferenciaDeAgente"/>, porque ele pode ser cópia de outro.
    /// </summary>
    public bool QuerMudar(AgenteEncontrado achado, Func<ComputadorSala, string?> nomeOuvidoEm) => DonoEmOutroLugar(achado, nomeOuvidoEm) != null;

    /// <summary>
    /// Um PC anunciado e conferido estava na lista em outro endereço: ele vai para o lugar certo, mesmo que o endereço novo fosse de
    /// outro computador, e os dois trocam. Devolve quem mudou, para reconectar.
    /// <paramref name="nomeOuvidoEm"/> diz que nome foi ouvido há pouco no endereço de um computador da lista. Se o endereço antigo
    /// ainda anuncia o mesmo nome, são dois PCs com nome igual, e nada muda: a lista ficaria trocando sem parar.
    /// </summary>
    public IReadOnlyList<ComputadorSala> Reorganizar(AgenteEncontrado achado, Func<ComputadorSala, string?> nomeOuvidoEm)
    {
        if (DonoEmOutroLugar(achado, nomeOuvidoEm) is not { } dono)
        {
            Aprender(achado);
            return [];
        }

        var ocupante = NoLugar(achado);
        (string endereco, int porta) = (dono.Endereco, dono.Porta);
        dono.Endereco = achado.Endereco;
        dono.Porta = achado.Porta;
        dono.NomeNaRede = achado.Nome;
        if (ocupante != null)
        {
            ocupante.Endereco = endereco;   // fica no endereço livre até se anunciar também
            ocupante.Porta = porta;
        }
        Salvar();
        return ocupante == null ? [dono] : [dono, ocupante];
    }

    /// <summary>Guarda como o PC daquele endereço se chama na rede. Na primeira vez, o nome do Windows pode ser diferente do nome da lista.</summary>
    public void Aprender(AgenteEncontrado achado)
    {
        string nome = NomeComparavel(achado.Nome);
        if (nome.Length == 0 || NoLugar(achado) is not { } ocupante) return;
        if (Identidade(ocupante) == nome || (ocupante.NomeNaRede == null && !_computadores.Any(c => Identidade(c) == nome)))
            Lembrar(ocupante, achado.Nome);
    }

    ComputadorSala? DonoEmOutroLugar(AgenteEncontrado achado, Func<ComputadorSala, string?> nomeOuvidoEm)
    {
        string nome = NomeComparavel(achado.Nome);
        if (nome.Length == 0 || (NoLugar(achado) is { } ocupante && Identidade(ocupante) == nome)) return null;
        var donos = _computadores.Where(c => Identidade(c) == nome).ToList();
        if (donos.Count != 1) return null;
        return nomeOuvidoEm(donos[0]) is { } ouvido && NomeComparavel(ouvido) == nome ? null : donos[0];
    }

    /// <summary>
    /// A conexão deste computador caiu em outro PC, que pegou o IP dele? O que vier por ela (MAC, por exemplo) não é deste computador.
    /// Só dá para saber depois que o PC da lista se anunciou uma vez.
    /// </summary>
    public bool EhOutroPc(ComputadorSala c, string nomeConectado) => c.NomeNaRede is { } naRede && !MesmoNome(naRede, nomeConectado);

    void Lembrar(ComputadorSala c, string nome)
    {
        if (c.NomeNaRede == nome) return;
        c.NomeNaRede = nome;
        Salvar();
    }

    /// <summary>Troca os dados por um objeto novo (quem usa a lista percebe que o computador mudou). Mantém a posição.</summary>
    public ComputadorSala Atualizar(ComputadorSala atual, string nome, string endereco, int porta)
    {
        int i = _computadores.IndexOf(atual);
        if (i < 0) throw new ArgumentException("Esse computador não está mais na sala.");
        if (Validar(nome, endereco, porta, atual) is { } erro) throw new ArgumentException(erro);
        var novo = new ComputadorSala { Nome = nome.Trim(), Endereco = endereco.Trim(), Porta = porta, Mac = atual.Mac, NomeEscolhido = atual.NomeEscolhido, NomeNaRede = atual.NomeNaRede };
        _computadores[i] = novo;
        Salvar();
        return novo;
    }

    public bool Remover(ComputadorSala c)
    {
        if (!_computadores.Remove(c)) return false;
        Salvar();
        _removidos.Add(NomeComparavel(c.Nome));
        if (c.NomeEscolhido is { } escolhido) _removidos.Add(NomeComparavel(escolhido));
        if (c.NomeNaRede is { } naRede) _removidos.Add(NomeComparavel(naRede));
        SalvarRemovidos();
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

    /// <summary>
    /// Usa o nome escolhido na instalação do PC. Cada nome vale uma vez: se o professor renomear depois, a escolha dele fica
    /// até alguém escolher outro nome no PC. Retorna true se o nome na lista mudou.
    /// </summary>
    public bool AplicarNomeEscolhido(ComputadorSala c, string nome)
    {
        if (c.NomeEscolhido == nome || !_computadores.Contains(c)) return false;
        // O PC que conectou aqui é outro da lista (pegou este IP): quem resolve é o Reorganizar, não uma troca de nome.
        string identidade = NomeComparavel(nome);
        if (_computadores.Any(o => o != c && Identidade(o) == identidade)) return false;
        bool mudou = c.Nome != nome;
        c.NomeEscolhido = nome;
        c.NomeNaRede = nome;
        c.Nome = nome;
        Salvar();
        return mudou;
    }

    /// <summary>
    /// O computador da lista que apareceu na rede em outro endereço (o IP dele mudou), achado pelo nome sem contar espaços,
    /// hífens e maiúsculas: o "PC18" do Windows é o "PC 18" da lista. Null se o endereço já é de alguém ou se o nome não for único.
    /// </summary>
    public ComputadorSala? MudouDeEndereco(AgenteEncontrado achado)
    {
        if (NoLugar(achado) != null) return null;
        string nome = NomeComparavel(achado.Nome);
        var mesmos = _computadores.Where(c => c.Porta == achado.Porta && NomeComparavel(c.Nome) == nome).ToList();
        return mesmos.Count == 1 ? mesmos[0] : null;
    }

    static string NomeComparavel(string nome) => string.Concat(nome.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /// <summary>O mesmo nome, sem contar espaços, hífens e maiúsculas: "PC18" é "PC 18".</summary>
    public static bool MesmoNome(string a, string b) => NomeComparavel(a) == NomeComparavel(b);

    /// <summary>Troca só o endereço, no mesmo objeto: a conexão aberta para este computador passa a usar o novo.</summary>
    public void DefinirEndereco(ComputadorSala c, string endereco)
    {
        if (!_computadores.Contains(c)) return;
        c.Endereco = endereco;
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

    HashSet<string> LerRemovidos()
    {
        try
        {
            if (File.Exists(CaminhoRemovidos))
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(CaminhoRemovidos)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return [];
    }

    void SalvarRemovidos()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        File.WriteAllText(CaminhoRemovidos, JsonSerializer.Serialize(_removidos));
    }

    void Salvar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        string temporario = _caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(_computadores, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporario, _caminho, overwrite: true);
    }
}
