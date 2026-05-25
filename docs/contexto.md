# Contexto do Projeto

## Objetivo

Construir um aplicativo desktop para Windows que ajude a importar, analisar, listar, instalar e desinstalar pacotes Tizen já assinados em um Samsung Galaxy Watch Tizen via SDB.

## O que foi descoberto sobre o fluxo Tizen

- No Galaxy Watch Tizen, o caminho que funcionou foi:
  - `sdb push <arquivo>.wgt /tmp/<arquivo>.wgt`
  - `sdb shell pkgcmd -w -t wgt -p /tmp/<arquivo>.wgt`
- O `sdb install` normal não é o caminho principal para `.wgt` nesse cenário.
- O relógio valida assinatura Tizen normalmente.
- O app desktop respeita esse fluxo e não tenta alterar o pacote.

## Por que o app usa SDB e `pkgcmd -w`

O objetivo é facilitar o empacotamento operacional e o diagnóstico, sem esconder o comportamento real do alvo.

Para `.wgt`, o app usa o caminho direto validado no relógio. Para `.tpk`, o suporte fica ligado ao que o `pkgcmd` do dispositivo aceitar.

## Próximos passos possíveis

- Refinar o parser da saída de `pkgcmd` em diferentes versões de firmware.
- Melhorar a detecção de watchface e shell wrapper com heurísticas adicionais.
- Adicionar suporte a mais variações de layout de logs do SDB, se necessário.
- Ampliar a cobertura de testes automatizados para importação e análise.
