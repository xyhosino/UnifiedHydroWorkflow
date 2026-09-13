$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $compiler /nologo /target:library /platform:x86 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:tests\baseline\Baseline.dll tests\baseline\MainForm.cs
    if ($LASTEXITCODE -ne 0) { throw 'Baseline compilation failed' }
    & $compiler /nologo /target:exe /platform:x86 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll /out:tests\EnvironmentTests.exe MainForm.cs MainForm.Environment.cs MainForm.InputMapping.cs MainForm.ExternalSources.cs MainForm.Preprocessing.cs PreprocessingForm.cs EnvironmentChecker.cs DeploymentLayout.cs WorkflowRuntimeStager.cs MaintenanceManager.cs InputDiscovery.cs LauncherInputMapping.cs InputMappingForm.cs DateTimeSelectionForm.cs tests\EnvironmentTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & .\tests\EnvironmentTests.exe @args | Tee-Object -FilePath tests\TEST_RESULTS.txt
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }

    & $compiler /nologo /target:exe /platform:x86 /r:System.Xml.dll /out:tests\MappingTests.exe InputDiscovery.cs LauncherInputMapping.cs tests\MappingTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Mapping test compilation failed' }
    & .\tests\MappingTests.exe @args | Tee-Object -FilePath tests\MAPPING_TEST_RESULTS.txt
    if ($LASTEXITCODE -ne 0) { throw 'Mapping tests failed' }
}
finally { Pop-Location }
