# Compila e copia Traduttore.exe nel sito, insieme a Traduttore.txt (versione e impronta SHA-256)
# che il programma legge per aggiornarsi da solo. Prima aumenta Version in Traduttore.cs.
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
$site = Join-Path $src "..\docs"

& "$src\build.cmd"
if ($LASTEXITCODE -ne 0) { throw "compilazione non riuscita" }

$cs = Get-Content "$src\Traduttore.cs" -Raw -Encoding UTF8
if ($cs -notmatch 'public const string Version = "([0-9.]+)"') { throw "Version non trovata in Traduttore.cs" }
$ver = $Matches[1]

Copy-Item "$src\Traduttore.exe" "$site\download\Traduttore.exe" -Force
$hash = (Get-FileHash "$site\download\Traduttore.exe" -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$site\download\Traduttore.txt", "$ver`n$hash`n")

$js = [IO.File]::ReadAllText("$site\programmi.js")
$js = [regex]::Replace($js, '(nome: "Traduttore riquadro",\s*versione: ")[^"]*', "`${1}$ver")
[IO.File]::WriteAllText("$site\programmi.js", $js)

Write-Host "Pubblicata la versione $ver ($hash). Ora fai commit e push di docs."
