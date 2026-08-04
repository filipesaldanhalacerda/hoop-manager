# Hoop Connection Manager

Aplicativo WPF para instalar, autenticar e operar exclusivamente o `hoop.exe` oficial, listar conexões e manter túneis temporários.

## Uso

1. Execute o wizard e valide a instalação existente ou selecione um instalador oficial `.ps1`, `.cmd` ou `.bat`.
2. Faça login pelo comando `hoop login` e conclua a autenticação no navegador.
3. Carregue as conexões, conecte e copie os dados temporários para o DBeaver.
4. Desconecte pelo Dashboard ou encerre o aplicativo para finalizar todos os túneis.

Senhas e tokens não são gravados. O aplicativo não altera arquivos internos do DBeaver. Configurações não secretas e logs sanitizados ficam sob `%LocalAppData%\Hoop Connection Manager`.

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
