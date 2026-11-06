namespace ControleSala.Comum;

/// <summary>Quem erra a chave da sala várias vezes seguidas fica um tempo sem poder tentar, para dificultar a tentativa e erro.</summary>
public sealed class LimiteTentativas(int falhasAteBloquear = 5, TimeSpan? bloqueio = null, TimeProvider? tempo = null)
{
    const int EnderecosGuardados = 1000;

    readonly TimeSpan _bloqueio = bloqueio ?? TimeSpan.FromMinutes(1);
    readonly TimeProvider _tempo = tempo ?? TimeProvider.System;
    readonly Dictionary<string, (int Falhas, DateTimeOffset Ate)> _enderecos = [];
    readonly Lock _trava = new();

    public bool Bloqueado(string endereco)
    {
        lock (_trava)
            return _enderecos.TryGetValue(endereco, out var e) && e.Ate > _tempo.GetUtcNow();
    }

    public void Falhou(string endereco)
    {
        var agora = _tempo.GetUtcNow();
        lock (_trava)
        {
            if (_enderecos.Count >= EnderecosGuardados) _enderecos.Clear();
            _enderecos.TryGetValue(endereco, out var e);
            int falhas = e.Falhas + 1;
            _enderecos[endereco] = falhas >= falhasAteBloquear ? (0, agora + _bloqueio) : (falhas, default);
        }
    }

    public void Acertou(string endereco)
    {
        lock (_trava) _enderecos.Remove(endereco);
    }
}
