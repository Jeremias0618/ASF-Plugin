#!/usr/bin/env pwsh
# Verifies ASF git tag + that the plugin DLL references ArchiSteamFarm at the expected version.
param(
	[Parameter(Mandatory = $true)][string] $PluginDll,
	[Parameter(Mandatory = $true)][string] $ExpectedAsfVersion,
	[Parameter(Mandatory = $false)][string] $AsfCheckoutPath = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PluginDll)) {
	throw "Plugin DLL not found: $PluginDll"
}

$expected = [version]$ExpectedAsfVersion

if ($AsfCheckoutPath) {
	if (-not (Test-Path -LiteralPath $AsfCheckoutPath)) {
		throw "ASF checkout path not found: $AsfCheckoutPath"
	}

	Push-Location $AsfCheckoutPath
	try {
		$exact = git describe --tags --exact-match HEAD 2>$null
		if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($exact)) {
			throw "ASF checkout HEAD is not an exact tag (expected $ExpectedAsfVersion)"
		}
		if ($exact -ne $ExpectedAsfVersion) {
			throw "ASF checkout tag '$exact' != expected '$ExpectedAsfVersion'"
		}
		Write-Host "OK: ASF git tag is $exact"
	}
	finally {
		Pop-Location
	}
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("asf-bind-check-" + [guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
	Push-Location $work
	dotnet new console -n BindCheck -f net10.0 --force | Out-Null
	Set-Location (Join-Path $work "BindCheck")
	dotnet add package System.Reflection.MetadataLoadContext --version 9.0.0 | Out-Null

	@'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

static class Program {
	static int Main(string[] args) {
		if (args.Length != 2) {
			Console.Error.WriteLine("Usage: BindCheck <plugin.dll> <expectedAsfVersion>");
			return 2;
		}

		string pluginDll = Path.GetFullPath(args[0]);
		Version expected = Version.Parse(args[1]);
		string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
		string[] paths = Directory.GetFiles(runtimeDir, "*.dll")
			.Append(pluginDll)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var resolver = new PathAssemblyResolver(paths);
		using var mlc = new MetadataLoadContext(resolver);
		Assembly asm = mlc.LoadFromAssemblyPath(pluginDll);
		AssemblyName? asf = asm.GetReferencedAssemblies()
			.FirstOrDefault(a => string.Equals(a.Name, "ArchiSteamFarm", StringComparison.OrdinalIgnoreCase));

		if (asf is null) {
			Console.Error.WriteLine("FAIL: plugin does not reference ArchiSteamFarm");
			return 1;
		}

		Version? actual = asf.Version;
		Console.WriteLine($"Plugin references ArchiSteamFarm {actual}");
		if (actual is null || actual != expected) {
			Console.Error.WriteLine($"FAIL: expected ArchiSteamFarm {expected}, got {actual}");
			return 1;
		}

		Console.WriteLine($"OK: strong-name bind target matches ASF {expected}");
		return 0;
	}
}
'@ | Set-Content -LiteralPath "Program.cs" -Encoding utf8

	dotnet build -c Release --nologo
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to build bind checker"
	}

	& dotnet run -c Release --no-build -- $PluginDll $ExpectedAsfVersion
	if ($LASTEXITCODE -ne 0) {
		throw "ASF bind verification failed (compile-time reference mismatch)"
	}
}
finally {
	Pop-Location -ErrorAction SilentlyContinue
	Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
