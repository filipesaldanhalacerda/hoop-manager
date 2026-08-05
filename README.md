# Hoop Connection Manager

Aplicativo WPF para instalar, autenticar e operar exclusivamente o `hoop.exe` oficial, listar conexões e manter túneis temporários.

## Uso

1. Execute o wizard e valide a instalação existente ou selecione um instalador oficial `.ps1`, `.cmd` ou `.bat`.
2. Faça login pelo comando `hoop login` e conclua a autenticação no navegador.
3. Carregue as conexões, conecte e copie os dados temporários para o DBeaver.
4. Desconecte pelo Dashboard ou encerre o aplicativo para finalizar todos os túneis.

Senhas e tokens não são gravados. O aplicativo não altera arquivos internos do DBeaver. Configurações não secretas e logs sanitizados ficam sob `%LocalAppData%\Hoop Connection Manager`.

Quando o DBeaver já está aberto, a conexão é encaminhada para a janela existente — nenhuma janela adicional é criada.

## Segurança

| Dado | Onde vive | Por quanto tempo |
| --- | --- | --- |
| Senha temporária do túnel | Memória do processo e linha de comando do launcher do DBeaver | Enquanto o túnel existir |
| Token do Hoop | Gerenciado pelo próprio Hoop CLI | Fora do escopo deste aplicativo |
| Configurações e histórico | `%LocalAppData%\Hoop Connection Manager` | Até o usuário limpar |

**Risco aceito:** a senha temporária é passada ao DBeaver pela linha de comando (`-con ...|password=...`), que é a única interface oferecida pelo DBeaver para abrir uma conexão pronta. Durante os poucos segundos em que o launcher existe, qualquer processo do mesmo usuário consegue lê-la via `Win32_Process`, e agentes de EDR costumam registrar linhas de comando. O impacto é limitado porque a senha vale apenas para o túnel corrente, é descartada com ele e `savePassword=false` impede que o DBeaver a grave em disco.

A senha copiada para a área de transferência é removida automaticamente após 30 segundos.

## Diagnóstico

```powershell
dotnet restore
dotnet build
dotnet test
```

## Instalador corporativo

O MSI x64 autocontido pode ser gerado com:

```powershell
dotnet build installer\DevAccessCenter.Installer.wixproj -c Release -p:ProductVersion=1.0.0
```

Atualizações usam o mesmo `UpgradeCode` e preservam configurações, logs e histórico em `%LocalAppData%`. Consulte [installer/README.md](installer/README.md) para instalação silenciosa e publicação pela central corporativa.

O funcionamento real depende da versão corporativa do Hoop CLI, do login via navegador e das permissões de execução da máquina.
