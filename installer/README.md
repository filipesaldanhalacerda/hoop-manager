# Instalador corporativo

O projeto gera um MSI x64 autocontido, adequado para implantação por Intune, SCCM ou outra central corporativa.

## Gerar o MSI

```powershell
dotnet build installer\DevAccessCenter.Installer.wixproj -c Release -p:ProductVersion=1.0.0
```

O pacote será criado em `installer\bin\x64\Release\DevAccessCenter-1.0.0-x64.msi`.

## Instalação silenciosa

```powershell
msiexec /i DevAccessCenter-1.0.0-x64.msi /qn /norestart
```

## Atualização

Gere o próximo pacote com uma versão maior, por exemplo `-p:ProductVersion=1.1.0`, e instale normalmente. O `UpgradeCode` fixo identifica a versão anterior e o MSI realiza a atualização no mesmo diretório. Downgrade é bloqueado.

Configurações, logs e histórico ficam em `%LocalAppData%\Hoop Connection Manager`, fora de `Program Files`. Instalação, atualização e desinstalação não removem esses dados do perfil do usuário.
