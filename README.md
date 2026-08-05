# Hoop Connection Manager

Aplicativo WPF para instalar, autenticar e operar exclusivamente o `hoop.exe` oficial, listar conexões e manter túneis temporários.

## Uso

1. Execute o wizard. Se o Hoop não for encontrado, o próprio assistente instala pelo script oficial da companhia, que acompanha o aplicativo.
2. Faça login pelo comando `hoop login` e conclua a autenticação no navegador.
3. Carregue as conexões e conecte. Em **Dados**, copie host, porta, database, usuário, senha ou a URL JDBC.
4. Cole no gerenciador de banco de sua preferência.
5. Desconecte pelo Dashboard ou encerre o aplicativo para finalizar todos os túneis.

## Instalação do Hoop CLI

O script oficial fica embutido em [`Resources/Scripts/install-hoop.ps1`](HoopConnectionManager/Resources/Scripts/install-hoop.ps1) e é executado pelo assistente quando o Hoop não é encontrado. Ele baixa a versão indicada de `releases.hoop.dev`, extrai em `%UserProfile%\hoop` e registra a pasta no PATH do usuário. **Não exige privilégio de administrador** e não altera nada fora do perfil.

Como o script é uma cópia do que a companhia distribui, ele precisa ser atualizado aqui quando a versão de referência mudar. O assistente também aceita executar um instalador escolhido manualmente, pelo mesmo serviço.

## Escopo

O aplicativo cuida do túnel e entrega os dados da conexão. **Ele não inicia nem configura nenhum cliente de banco** — a escolha da ferramenta é do desenvolvedor, e o aplicativo não toca em arquivos de cliente algum.

A abertura automática do DBeaver existiu e foi removida: ela dependia de cada instalação cooperar com o encaminhamento de linha de comando, e as variações (instalador tradicional, pacote MSIX da Microsoft Store) produziam janelas duplicadas e conexões que não chegavam ao destino.

Cada túnel recebe uma porta local exclusiva a partir da 5433. **Porta e senha mudam a cada túnel**, então uma conexão salva no cliente precisa ser conferida a cada sessão.

## Segurança

| Dado | Onde vive | Por quanto tempo |
| --- | --- | --- |
| Senha temporária do túnel | Memória do processo, e na área de transferência quando copiada | Enquanto o túnel existir |
| Token do Hoop | Gerenciado pelo próprio Hoop CLI | Fora do escopo deste aplicativo |
| Configurações e histórico | `%LocalAppData%\Hoop Connection Manager` | Até o usuário limpar |

Senhas e tokens não são gravados em disco pelo aplicativo. A senha copiada para a área de transferência é removida automaticamente após 30 segundos. Configurações não secretas e logs sanitizados ficam sob `%LocalAppData%\Hoop Connection Manager`.

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
