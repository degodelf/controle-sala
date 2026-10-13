"""Gera controlesala.ico (monitor com ponto verde) sem dependências. Uso: python gerar_icone.py"""
import struct, zlib

BASE = 256
SS = 3  # sobreamostragem 3x3 para bordas suaves


def dentro_retangulo_arredondado(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def pixel(x, y):
    """Cor RGBA (0-255) do ponto (x, y) do desenho de 256x256."""
    azul, branco, tela, verde = (31, 58, 95), (240, 240, 240), (45, 45, 48), (76, 175, 80)
    if not dentro_retangulo_arredondado(x, y, 0, 0, 256, 256, 52):
        return (0, 0, 0, 0)
    cor = azul
    if dentro_retangulo_arredondado(x, y, 36, 52, 220, 172, 16):   # moldura do monitor
        cor = branco
        if dentro_retangulo_arredondado(x, y, 50, 66, 206, 158, 6):  # tela
            cor = tela
            if (x - 128) ** 2 / 38.0 ** 2 + (y - 112) ** 2 / 26.0 ** 2 <= 1:  # luz verde
                cor = verde
    elif 116 <= x <= 140 and 172 <= y <= 198:                         # pescoço
        cor = branco
    elif dentro_retangulo_arredondado(x, y, 78, 196, 178, 214, 8):    # base
        cor = branco
    return (*cor, 255)


def desenhar(tamanho):
    """Imagem tamanho x tamanho em RGBA, desenhando o modelo de 256 com sobreamostragem."""
    escala = BASE / tamanho
    linhas = []
    for j in range(tamanho):
        linha = []
        for i in range(tamanho):
            r = g = b = a = 0.0
            for sj in range(SS):
                for si in range(SS):
                    px = (i + (si + 0.5) / SS) * escala
                    py = (j + (sj + 0.5) / SS) * escala
                    pr, pg, pb, pa = pixel(px, py)
                    r += pr * pa; g += pg * pa; b += pb * pa; a += pa
            n = SS * SS
            if a > 0:
                linha.append((round(r / a), round(g / a), round(b / a), round(a / n)))
            else:
                linha.append((0, 0, 0, 0))
        linhas.append(linha)
    return linhas


def png(linhas):
    h, w = len(linhas), len(linhas[0])
    cru = b"".join(b"\x00" + b"".join(bytes(p) for p in l) for l in linhas)
    def bloco(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + bloco(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + bloco(b"IDAT", zlib.compress(cru, 9)) + bloco(b"IEND", b""))


def bmp_dib(linhas):
    """Entrada de ICO em formato DIB 32 bits (as menores usam este formato por compatibilidade)."""
    h, w = len(linhas), len(linhas[0])
    cabecalho = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b"".join(bytes((b, g, r, a)) for l in reversed(linhas) for (r, g, b, a) in l)
    mascara = b"\x00" * (((w + 31) // 32) * 4 * h)
    return cabecalho + pixels + mascara


imagens = []
for t in (16, 24, 32, 48, 64, 256):
    l = desenhar(t)
    imagens.append((t, png(l) if t == 256 else bmp_dib(l)))

cab = struct.pack("<HHH", 0, 1, len(imagens))
offset = 6 + 16 * len(imagens)
dirs, dados = b"", b""
for t, d in imagens:
    dirs += struct.pack("<BBBBHHII", 0 if t == 256 else t, 0 if t == 256 else t, 0, 0, 1, 32, len(d), offset)
    offset += len(d)
    dados += d
open("controlesala.ico", "wb").write(cab + dirs + dados)
print("controlesala.ico:", len(cab + dirs + dados), "bytes")
