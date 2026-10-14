# Roteiro de teste na escola (2 PCs reais)

Folha de conferência para validar o ControleSala na sala de verdade. Marque `[x]` no que funcionar
e escreva o que der errado no fim (seção **Problemas**), para corrigirmos depois.

> **Antes de mexer num PC de aluno de verdade, faça um teste de limpeza com um arquivo que pode apagar**
> (crie um `teste.txt` na Área de Trabalho do aluno). Nunca teste a limpeza com arquivos de verdade.

## O que levar

- [ ] Um pendrive.
- [ ] O instalador `ControleSala-Setup-<versão>.exe` (do GitHub, ou gerado por você).
- [ ] Caso o teste envolva atualização: também uma versão **mais antiga** do instalador.
- [ ] As senhas: a de **administrador** dos PCs e a **senha do pacote** da sala.

---

## Parte 1 — PC do PROFESSOR

1. [ ] Instale o `ControleSala-Setup` escolhendo **Professor**. (Precisa da senha de administrador.)
2. [ ] Abra o programa. Cadastre o **primeiro professor** (nome + PIN) no primeiro acesso.
3. [ ] Clique em **Sala...** → **Criar sala** (nome da escola + nome da sala).
4. [ ] Clique em **Papel de parede...**, escolha o fundo e a logo da escola, e feche (salva sozinho).
5. [ ] Em **Sala... → Gerar pacote para os alunos...**, escolha uma **senha do pacote** e salve o
       arquivo `.controlesala` no **pendrive**, junto com o `ControleSala-Setup`.
6. [ ] **Backup da chave de assinatura** (uma vez só, importante): no PC do professor rode
       `ControleSala.Assinador exportar backup-chave.cscha` e guarde o arquivo + a senha fora do PC.

## Parte 2 — PC de um ALUNO (pelo pendrive)

7. [ ] Entre no Windows na conta **aluno** (a sem senha, que os alunos usam).
8. [ ] Rode o `ControleSala-Setup` do pendrive → escolha **Aluno**. (Vai pedir a senha de administrador.)
9. [ ] Confirme que ele achou o arquivo da sala e mostrou **"Este computador fará parte da sala X"**.
10. [ ] Conclua a instalação.
11. [ ] **Apague o `.controlesala` do pendrive** depois de instalar (não deixe a chave rodando por aí).

## Parte 3 — Conexão e vídeo

12. [ ] No PC do professor, o PC do aluno **apareceu sozinho** na grade? (ou use **Procurar na rede...**).
13. [ ] A **miniatura** mostra a tela do aluno ao vivo? A bolinha está **verde**?
14. [ ] **Duplo clique** na tela do aluno → abre em tamanho grande. Está **fluida** (uns 30 fps)?
15. [ ] Peça para alguém mexer no PC do aluno (mover o mouse, abrir algo) e veja se aparece na tela do professor.
16. [ ] Anote o **FPS** e os **Mbps** que aparecem embaixo da tela grande: ____ fps / ____ Mbps.

## Parte 4 — Ícone no PC do aluno (nunca testamos isto)

17. [ ] No PC do aluno, perto do relógio (ou na setinha **^** de ícones ocultos), aparece o **ícone do
        ControleSala** (um monitor)?
18. [ ] Enquanto o professor está vendo a tela, o ícone mostra que a tela **está sendo vista**?
19. [ ] Clicando no ícone, abre só o **cartão informativo** (sala, escola, quem está vendo)? **Sem** botões de configuração?

## Parte 5 — Aluno não consegue burlar (na conta "aluno")

20. [ ] Abra o **Gerenciador de Tarefas** → tente **Finalizar** `ControleSala.Agente.exe` → deve dar **"Acesso negado"**.
21. [ ] Tente **desinstalar** pelo "Adicionar ou remover programas" → deve **pedir administrador** (e não deixar).
22. [ ] Se encerrar o processo de algum jeito, ele **volta sozinho** em poucos segundos?

## Parte 6 — Transferência de arquivos

23. [ ] No professor, **Enviar arquivos...** → escolha um PDF ou imagem → marque o PC do aluno → **Enviar**.
24. [ ] No PC do aluno, o arquivo chegou na pasta **"Arquivos do Professor"**, na Área de Trabalho?
25. [ ] Apareceu o **aviso** perto do relógio avisando que chegou arquivo?
26. [ ] Tente enviar um programa (`.exe`) → deve ser **recusado**.

## Parte 7 — Comandos

27. [ ] **Abrir link** (ex.: um site da escola) → abre no navegador do aluno?
28. [ ] **Enviar mensagem** → aparece o aviso no canto da tela do aluno, com o seu nome?
29. [ ] **Bloquear a tela** → a tela do aluno fica coberta com o recado? O Alt+F4 **não** fecha?
30. [ ] **Desbloquear a tela** → volta ao normal?
31. [ ] **Programas permitidos...** → cadastre a Calculadora. **Abrir programa** → abre no aluno? **Fechar programa** → fecha?
32. [ ] **Silenciar o som** → o som do aluno fica mudo? **Reativar** → volta?
33. [ ] **Bloquear sites da lista** (cadastre um site em "Sites bloqueados...") → no aluno, o site não abre e o resto funciona? **Liberar a internet** → volta?
34. [ ] **Bloquear TODA a internet** → o aluno perde a internet mas o professor continua vendo a tela? **Liberar** → volta?

## Parte 8 — Limpeza (com arquivo de teste!)

35. [ ] Na conta do aluno, crie um `teste.txt` na **Área de Trabalho** e em **Downloads**.
36. [ ] No professor, **Comandos → Limpeza automática (configurar)**: marque o que apagar, ponha uma
        exceção se quiser, salve.
37. [ ] **Comandos → Limpar agora** → confirme. Apareceu a **tela de limpeza** no PC do aluno?
38. [ ] Os `teste.txt` foram apagados? O que estava na **exceção** foi **preservado**?
39. [ ] (Se marcou) O **papel de parede** foi restaurado e o **Windows Defender** começou a atualizar/verificar?

## Parte 9 — Desligar / reiniciar (cuidado)

40. [ ] **Comandos → Reiniciar os computadores** → confirme → o aluno vê o **aviso de 20 segundos** e reinicia?
41. [ ] Depois que o PC do aluno volta, ele **reaparece sozinho** na grade do professor?

## Parte 10 — Wake-on-LAN (se a BIOS permitir)

42. [ ] Desligue o PC do aluno. No professor, **Comandos → Ligar os computadores (Wake-on-LAN)** → marque o PC → **Ligar**.
43. [ ] O PC do aluno **ligou** sozinho? (Precisa do Wake-on-LAN ativado na BIOS da placa de rede.)

## Parte 11 — Atualização (se tiver 2 versões)

44. [ ] Com uma versão mais antiga no aluno, use **Atualizar alunos** (ou o aviso de nova versão) e envie a nova.
45. [ ] O aluno **instalou** e **voltou** com a versão nova (a grade mostra a versão atualizada)?

---

## Problemas encontrados (anote aqui)

| Parte | O que deveria acontecer | O que aconteceu |
|-------|-------------------------|-----------------|
|       |                         |                 |
|       |                         |                 |
|       |                         |                 |

## Medições

- Rede da sala: ______ (cabo? Wi-Fi? velocidade?)
- FPS na tela cheia: ______ | Banda: ______ Mbps
- Quantos PCs ao mesmo tempo sem travar: ______
