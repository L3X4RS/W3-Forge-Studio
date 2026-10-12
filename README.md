# W3 Forge Studio

**Versão-base: v0.6.1 Beta (11/10/2026)**

Ferramenta de modding para **The Witcher 3 (PC / Next-Gen)**, desenvolvida em C# com WPF e componentes de edição REDengine. Este repositório contém o **código-fonte**, não um instalador ou uma cópia do jogo.

## Funcionalidades atuais

- Navegação por arquivos e projetos `.w3modproj`, extração de recursos e criação de mods.
- Editor de texto para XML, CSV, scripts WitcherScript (`.ws`), JSON, INI e outros formatos.
- Exportação para JSON editável e recompilação para RAW nos formatos validados.
- Navegação e edição com W3Edit/CR2W quando as dependências estão instaladas.
- Exportação de textura XBM para PNG, TGA e DDS em casos suportados.
- Empacotamento e instalação de mods, incluindo projetos que contêm apenas scripts.
- Comando para executar The Witcher 3 usando a instalação local configurada.

## Limitações importantes

- Versão **Beta**: faça backups antes de editar mods.
- Não há painel de edição ENV visual em tempo real dentro do jogo.
- Não existe visualizador 3D completo nem exportação universal de malhas, ossos e materiais.
- PNG/DDS → XBM cozido e reconstrução de `texture.cache` **não estão implementados**.
- A edição de C++/C# é textual; o Forge não compila esse código como mod nativo do jogo.
- O empacotamento de `.ws` foi validado em testes isolados, mas scripts arbitrários dependem do compilador do jogo.
- A versão 0.6.1 **não** foi atestada como aprovada pela moderação/varredura do Nexus Mods.

## Estrutura

- `src/` — código-fonte do aplicativo e recursos WPF.
- `assets/` — identidade visual e ícone.
- `docs/` — documentação, notas da versão, avisos de terceiros e licenças.
- `build.ps1` — compilação local em Windows.

## Compilação (Windows)

Requisitos: Windows 10/11, .NET SDK atualizado e .NET Framework 4.8 / referências de desenvolvimento.

O editor integrado usa componentes externos do projeto W3Edit/WolvenKit e bibliotecas adicionais. **Eles não são redistribuídos neste repositório.** Para compilar:

1. Providencie legalmente as bibliotecas W3Edit e demais DLLs compatíveis, respeitando as licenças de cada projeto.
2. Disponibilize as DLLs do W3Edit e suas dependências em `ReferenceRuntime/` ou aponte `W3FORGE_W3EDIT_REFERENCE` para uma pasta local contendo as bibliotecas exigidas por `build.ps1`.
3. Providencie também `Pfim.dll` na pasta local `third_party_bin/Pfim.0.9.1/lib/netstandard2.0/`, conforme a estrutura esperada pelo script. Essa pasta é ignorada pelo Git.
4. Execute no PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

A saída compilada fica em `bin/` (ignorada pelo Git). O arquivo `docs/W3ForgeStudio.exe.config` é usado na etapa de montagem.

## Origem, licenças e créditos

O pacote de distribuição v0.6.1 inclui o texto da licença **GNU GPL v3** em `LICENSE`. Dependências de terceiros têm atribuições e licenças próprias em `docs/THIRD_PARTY_NOTICES.txt` e `docs/Licenses/`.

Créditos ao autor original do W3Edit, **Sarcen**, e aos colaboradores da linhagem WolvenKit. W3 Forge Studio não é afiliado ou endossado pela CD PROJEKT RED. The Witcher e REDengine pertencem aos respectivos titulares.

**Aviso:** revise a titularidade e as licenças de todos os componentes antes de tornar este repositório público.

## Segurança e arquivos excluídos

O Git não deve conter builds assinados ou não assinados, executáveis, DLLs de terceiros, dumps de jogos, arquivos mod, caches, backups, logs, credenciais nem instaladores. Consulte o `.gitignore`.

Documentação adicional: `docs/JSON_ROUNDTRIP.md`, `docs/CHANGELOG.txt` e `docs/SECURITY_RELEASE_NOTES.txt`.