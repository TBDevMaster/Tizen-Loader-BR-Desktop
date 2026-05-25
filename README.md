# Tizen Loader BR Desktop

Aplicativo desktop em C#/.NET 8/WPF para importar, analisar, listar, instalar e desinstalar pacotes Tizen já assinados em um Samsung Galaxy Watch Tizen via SDB.

## Objetivo

O fluxo principal de instalação é via SDB, usando o caminho validado no relógio:

```text
sdb push <arquivo>.wgt /tmp/<arquivo>.wgt
sdb shell pkgcmd -w -t wgt -p /tmp/<arquivo>.wgt
```

O app não usa Android, ADB nem Samsung Accessory como caminho principal.

## Como abrir no Visual Studio

1. Abra `TizenLoaderBRDesktop.sln`.
2. Restaure os pacotes NuGet, se o Visual Studio não fizer isso automaticamente.
3. Compile o projeto `TizenLoaderBRDesktop`.

## Como compilar

```bash
dotnet build .\TizenLoaderBRDesktop.sln
```

## Como conectar o relógio

1. Instale o Tizen Studio no Windows.
2. Verifique o caminho do `sdb.exe` em `Configurações`.
3. Clique em `Atualizar dispositivos`.
4. Se houver mais de um alvo, selecione o relógio correto na lista.

## Como importar e analisar um `.wgt`

1. Vá para a aba `Biblioteca`.
2. Clique em `Importar arquivo`.
3. Selecione um `.wgt`, `.tpk` ou `.zip`.
4. O app calcula SHA-256, lê `config.xml`, `tizen-manifest.xml`, `author-signature.xml` e `signature1.xml`, e mostra os avisos de análise.

Se o arquivo for `.zip`, o app procura recursivamente por `.wgt` e `.tpk` internos e ignora lixo comum como `__MACOSX`, `.DS_Store` e thumbnails.

## Como instalar usando SDB

1. Conecte o relógio.
2. Selecione o dispositivo alvo.
3. Escolha um item assinado ou confirme a tentativa, se o pacote não tiver assinatura detectada.
4. Clique em `Instalar no relógio`.

Para `.wgt`, o app usa `sdb push` seguido de `sdb shell pkgcmd -w -t wgt -p ...`.

Para `.tpk`, o app tenta o caminho equivalente via `pkgcmd` suportado pelo alvo.

## Como desinstalar um app

1. Vá para a aba `Dispositivos`.
2. Clique em `Listar apps instalados`.
3. Selecione o aplicativo.
4. Clique em `Desinstalar selecionado`.

O fluxo usa `pkgcmd -u -n <pkgid>`.

## Limitações conhecidas

- O app não altera nem re-assina pacotes.
- Não faz bypass de certificado.
- Pacotes sem assinatura podem falhar na instalação.
- O suporte a `.tpk` depende do que o `pkgcmd` do alvo aceitar.
- O XDA é tratado apenas como fonte comunitária aberta no navegador externo, sem scraping automático.

## Armazenamento local

A biblioteca, configurações e logs ficam em `%LocalAppData%\TizenLoaderBRDesktop`.
