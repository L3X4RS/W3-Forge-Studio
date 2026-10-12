# Conversão JSON ↔ RAW

Este guia se aplica ao W3 Forge Studio v0.6.1 Beta.

## Fluxo básico

1. Abra seu projeto `.w3modproj` e escolha um arquivo na árvore de recursos RAW.
2. No menu de contexto do arquivo, selecione **Export to editable JSON**.
3. Altere o conteúdo permitido no JSON exportado (especialmente os campos `value`), preservando identificadores, tipos e estrutura.
4. Salve e execute **Compile JSON to original RAW** no arquivo correspondente.
5. Revise os backups e valide o mod no jogo. Não edite diretamente os originais do jogo.

## Cobertura verificada

O fluxo de exportação, alteração controlada e retorno foi testado para `.env`, `.w2w`, `.csv`, `.xml` e `.ws` em cópias isoladas. Leitura/exportação sem modificação foi testada também para `.w2l` e `.w2anims`; isto **não** significa suporte completo a alterações nesses formatos.

O sistema valida hash, cria backups e busca evitar aplicar um JSON sobre um RAW incompatível. Não há garantia universal de round-trip em todos os recursos REDengine.

## Cuidados

- Faça cópia externa do projeto antes de editar.
- Não altere IDs estruturais ou tipos arbitrariamente.
- Algumas mudanças exigem compilação dos scripts pelo próprio jogo ou recursos externos.
- Nenhum painel de ENV em tempo real no jogo é incluído nesta versão.

Este documento descreve o fluxo de uso sem reproduzir caminhos ou arquivos privados do ambiente de desenvolvimento.