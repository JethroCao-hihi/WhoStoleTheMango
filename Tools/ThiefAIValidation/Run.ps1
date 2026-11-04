param([string]$UnityDataPath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectFile = Join-Path $projectRoot 'Assembly-CSharp.csproj'
if (-not (Test-Path -LiteralPath $projectFile)) { throw 'Open the project in Unity and generate its C# project files first.' }
[xml]$projectXml = Get-Content -LiteralPath $projectFile
if (-not $UnityDataPath) {
    $engineHint = $projectXml.SelectNodes('//Reference/HintPath') |
        Where-Object { $_.InnerText -match '[\\/]Data[\\/]Managed[\\/]UnityEngine[\\/]UnityEngine.CoreModule.dll$' } |
        Select-Object -First 1
    if (-not $engineHint) { throw 'Pass -UnityDataPath pointing to the Editor/Data folder of Unity 6000.3.8f1.' }
    $UnityDataPath = $engineHint.InnerText -replace '[\\/]Managed[\\/]UnityEngine[\\/]UnityEngine.CoreModule.dll$', ''
}
$validationRoot = Join-Path $projectRoot 'Temp\ThiefAIValidation'
$testProjectPath = Join-Path $validationRoot 'UnityProject'
New-Item -ItemType Directory -Path "$testProjectPath\Assets\Plugins", "$testProjectPath\Assets\Editor", "$testProjectPath\Packages", "$testProjectPath\ProjectSettings" -Force | Out-Null
Copy-Item -LiteralPath "$projectRoot\ProjectSettings\ProjectVersion.txt", "$projectRoot\ProjectSettings\TagManager.asset" -Destination "$testProjectPath\ProjectSettings"
Set-Content -LiteralPath "$testProjectPath\Packages\manifest.json" -Encoding UTF8 -Value '{"dependencies":{"com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.ui":"1.0.0"}}'
$references = @($projectXml.SelectNodes('//Reference/HintPath') | ForEach-Object { $_.InnerText })
foreach ($reference in $projectXml.SelectNodes('//ProjectReference')) {
    $name = [IO.Path]::GetFileNameWithoutExtension($reference.Include)
    if ($name -eq 'Assembly-CSharp') { continue }
    $referencePath = Join-Path $projectRoot ('Library\ScriptAssemblies\' + $name + '.dll')
    if (Test-Path -LiteralPath $referencePath) { $references += $referencePath }
}
$sources = @(Get-ChildItem "$projectRoot\Assets\Script\gardenOwner" -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem "$projectRoot\Assets\Script\thief\AI" -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\Editor\\' } | ForEach-Object { $_.FullName })
$outputDll = "$testProjectPath\Assets\Plugins\ThiefValidationGameplay.dll"
$options = @('-nologo', '-target:library', '-langversion:9.0', '-nostdlib+',
    ('-define:' + $projectXml.SelectSingleNode('//DefineConstants').InnerText), ('-out:"' + $outputDll + '"'))
$options += $references | Select-Object -Unique | ForEach-Object { '-r:"' + $_ + '"' }
$options += $sources | ForEach-Object { '"' + $_ + '"' }
$responsePath = Join-Path $validationRoot 'Subset.rsp'
Set-Content -LiteralPath $responsePath -Value $options -Encoding UTF8
& "$UnityDataPath\NetCoreRuntime\dotnet.exe" "$UnityDataPath\DotNetSdkRoslyn\csc.dll" "@$responsePath"
if ($LASTEXITCODE -ne 0) { throw 'Gameplay compilation failed.' }
foreach ($name in @('Unity.InputSystem.dll', 'UnityEngine.UI.dll', 'UnityEditor.UI.dll')) {
    Copy-Item -LiteralPath "$projectRoot\Library\ScriptAssemblies\$name" -Destination "$testProjectPath\Assets\Plugins"
}
Copy-Item -LiteralPath "$PSScriptRoot\FullThiefValidationRunner.cs" -Destination "$testProjectPath\Assets"
Copy-Item -LiteralPath "$PSScriptRoot\ValidationEntry.cs", "$projectRoot\Assets\Script\thief\AI\Editor\ThiefAISetup.cs" -Destination "$testProjectPath\Assets\Editor"
$logPath = Join-Path $validationRoot 'FullGameplayValidation.log'
$unityExecutable = Join-Path (Split-Path -Parent $UnityDataPath) 'Unity.exe'
$unityArguments = @('-batchmode', '-nographics', '-noUpm', '-projectPath', ('"' + $testProjectPath + '"'),
    '-executeMethod', 'ValidationEntry.Run', '-logFile', ('"' + $logPath + '"'))
$unityProcess = Start-Process -FilePath $unityExecutable -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
if (-not $unityProcess.WaitForExit(300000)) {
    Stop-Process -Id $unityProcess.Id
    throw "Unity validation timed out. See $logPath"
}
$results = @(Select-String -LiteralPath $logPath -Pattern 'THIEF_VALIDATION' | ForEach-Object { $_.Line })
$results | ForEach-Object { Write-Output $_ }
$results | Set-Content -LiteralPath (Join-Path $validationRoot 'LastResult.txt') -Encoding UTF8
if ($unityProcess.ExitCode -ne 0 -or -not ($results -match 'ALL PASSED')) { throw "Validation failed. See $logPath" }
Write-Output "Passed. Full log: $logPath"
