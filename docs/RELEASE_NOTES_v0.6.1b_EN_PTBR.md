# W3 Forge Studio v0.6.1 Beta

**English / Português (Brasil)**

## English

**The Witcher 3 Next-Gen Modding Workspace**

W3 Forge Studio is an independent Windows tool for exploring, editing, converting, and packaging supported REDengine 3 resources for The Witcher 3. This is a **beta** build; keep backups of important projects.

### Features
- **Project Explorer:** Create, open, and organize `.w3modproj` projects.
- **REDengine resource browser:** Locate, inspect, and extract supported game resources with W3Edit components.
- **JSON roundtrip:** Export supported RAW files to editable JSON and compile changes back to the original format, with validation and backups.
- **Structured editors:** CSV, XML, JSON, INI, WitcherScript (`.ws`), and other text resources.
- **Texture tools:** Preview supported XBM textures and export PNG, TGA, and DDS.
- **Native packaging:** Create compatible REDengine bundles and metadata without using the legacy ModKit, REDkit, or `wcc_lite` for supported workflows.
- **Script workbench:** Create and package WitcherScript, including script-only projects.
- **Mod management:** Build, install, and uninstall supported mods.
- **Run Game:** Launch The Witcher 3 from the application.

### Changes in v0.6.1
- Project Explorer retains expanded folders and navigation after deleting files or backups.
- Open Project remembers the project directory independently from Open File.
- Improved CSV editor layout and horizontal scrolling.
- Added Run Game and New Script functions.
- Added script-only build and installation support.
- Improved temporary-file exclusions during packaging.
- Removed automatic changes to Windows Mark-of-the-Web download markers.

### Installation
1. Download `W3.Forge.Studio.v0.6.1.Beta.zip` from the Assets section.
2. Extract the full ZIP to a writable folder.
3. Run `W3ForgeStudio.exe` and configure your The Witcher 3 installation if automatic detection fails.
4. Keep the bundled Runtime folder and dependencies alongside the application.

**Requirements:** Windows 10/11; .NET Framework 4.8; an installed copy of The Witcher 3 for browsing game assets.

### Beta limitations
- Full 3D mesh viewing/export is not implemented.
- Importing PNG/DDS back into cooked XBM and rebuilding `texture.cache` are not implemented.
- Live in-game ENV editing is not included.
- Some REDengine formats support inspection or export only.
- Arbitrary WitcherScript compilation and behavior must be verified in the game.

**Source code:** https://github.com/L3X4RS/W3-Forge-Studio

**Credits:** Sarcen (original W3Edit) and the WolvenKit community. Third-party components retain their respective licenses; see the included notices. This community project is not affiliated with CD PROJEKT RED.

---

## Português (Brasil)

**Ambiente de desenvolvimento de mods para The Witcher 3 Next-Gen**

O W3 Forge Studio é uma ferramenta independente para Windows criada para facilitar a exploração, edição, conversão e compilação de recursos compatíveis da REDengine 3 em The Witcher 3. Esta é uma versão **Beta**: faça backup dos seus projetos.

### Funcionalidades
- **Explorador de projetos:** Criação, abertura e organização de projetos `.w3modproj`.
- **Navegação por recursos REDengine:** Pesquisa, inspeção e extração de arquivos compatíveis com componentes do W3Edit.
- **Conversão JSON ↔ RAW:** Exportação de recursos compatíveis para JSON editável e recompilação para o formato original, com validação e backups.
- **Editores estruturados:** CSV, XML, JSON, INI, WitcherScript (`.ws`) e outros arquivos de texto.
- **Ferramentas de texturas:** Visualização de XBM compatíveis e exportação para PNG, TGA e DDS.
- **Empacotamento nativo:** Criação de bundles e metadados compatíveis sem depender do ModKit antigo, REDkit ou `wcc_lite` nos fluxos suportados.
- **Editor de scripts:** Criação e empacotamento de WitcherScript, inclusive projetos somente com scripts.
- **Gerenciamento de mods:** Compilação, instalação e desinstalação de mods compatíveis.
- **Executar jogo:** Inicie The Witcher 3 diretamente no Forge.

### Novidades da v0.6.1
- O explorador preserva as pastas expandidas e a navegação após excluir arquivos e backups.
- A opção Open Project mantém o diretório do projeto separado do histórico de Open File.
- Melhorias no tamanho e na rolagem horizontal do editor CSV.
- Inclusão das funções Run Game e New Script.
- Suporte à compilação e instalação de projetos compostos apenas por scripts.
- Exclusão mais segura de arquivos temporários durante o empacotamento.
- Remoção de alterações automáticas nas marcações de segurança de downloads do Windows.

### Instalação
1. Baixe `W3.Forge.Studio.v0.6.1.Beta.zip` na seção Assets.
2. Extraia todo o ZIP para uma pasta com permissão de escrita.
3. Execute `W3ForgeStudio.exe` e configure a instalação de The Witcher 3 caso a detecção automática falhe.
4. Mantenha a pasta Runtime e suas dependências junto ao aplicativo.

**Requisitos:** Windows 10/11; .NET Framework 4.8; The Witcher 3 instalado para navegar pelos recursos do jogo.

### Limitações da versão Beta
- Visualização e exportação 3D completas ainda não estão disponíveis.
- Importação de PNG/DDS para XBM compilado e reconstrução de `texture.cache` não estão implementadas.
- Não há editor ENV visual em tempo real dentro do jogo.
- Alguns formatos REDengine permitem apenas inspeção ou exportação.
- Scripts personalizados devem ser testados no jogo.

**Código-fonte:** https://github.com/L3X4RS/W3-Forge-Studio

**Créditos:** Sarcen (W3Edit original) e comunidade WolvenKit. As bibliotecas de terceiros seguem suas respectivas licenças, disponíveis nos avisos incluídos. Projeto independente, sem afiliação oficial com a CD PROJEKT RED.