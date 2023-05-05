using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ingot;

public class Program
{
	private const string firelyLicenseFile = "firelyserver-license.json";
	private const string firelyLicensePath = @$"C:\firely\{Program.firelyLicenseFile}";
	private const string firelyAppSettingsFile = "appsettings.instance.json";
	private const string dockerScriptFile = "docker_run.ps1";

	public static async Task Main(string[] args)
	{
		DirectoryInfo importDirectory = new("vonk-import.R4");

		if (!importDirectory.Exists)
		{
			importDirectory.Create();
		}

		foreach (string package in args)
		{
			string[] tokens = package.Split('@');
			if (tokens.Length != 2)
			{
				throw new ArgumentException();
			}

			await Program.InstallPackage(tokens[0], tokens[1], importDirectory);
		}

		Program.CleanUpPackages(importDirectory);
		Program.WriteFirelyLicense();
		await Program.WriteFirelyAppSettings();
		await Program.WriteDockerScript();
	}

	private static async Task InstallPackage(string name, string version, DirectoryInfo importDirectory)
	{
		ProcessStartInfo processStartInfo = new("fhir", $"install {name} {version} --here")
		{
			WorkingDirectory = importDirectory.FullName
		};

		Process? fhirProcess = Process.Start(processStartInfo);

		if (fhirProcess != null)
		{
			await fhirProcess.WaitForExitAsync();
		}
	}

	private static void CleanUpPackages(DirectoryInfo importDirectory)
	{
		DirectoryInfo? dependenciesDirectory = importDirectory.EnumerateDirectories("dependencies").FirstOrDefault();

		if (dependenciesDirectory == null)
		{
			throw new InvalidOperationException();
		}

		DirectoryInfo[] packageRootDirectories = dependenciesDirectory.GetDirectories();

		foreach (DirectoryInfo packageRootDirectory in packageRootDirectories)
		{
			if (packageRootDirectory.Name.StartsWith("hl7.fhir.r4.core#"))
			{
				packageRootDirectory.Delete(true);
			}
			else
			{
				DirectoryInfo? packageDirectory = packageRootDirectory.EnumerateDirectories("package").FirstOrDefault();

				if (packageDirectory == null)
				{
					continue;
				}

				DirectoryInfo? examplesDirectory = packageDirectory.EnumerateDirectories("examples").FirstOrDefault();

				if (examplesDirectory != null)
				{
					examplesDirectory.Delete(true);
				}
			}
		}
	}

	private static void WriteFirelyLicense()
	{
		File.Copy(Program.firelyLicensePath, Program.firelyLicenseFile, true);
	}

	private static async Task WriteFirelyAppSettings()
	{
		if (File.Exists(Program.firelyAppSettingsFile))
		{
			return;
		}

		FirelyAppSettings firelyAppSettings = new();

		JsonSerializerOptions jsonSerializerOptions = new(JsonSerializerDefaults.General)
		{
			WriteIndented = true
		};

		await using Stream stream = File.Open(Program.firelyAppSettingsFile, FileMode.Create, FileAccess.Write);
		await JsonSerializer.SerializeAsync(stream, firelyAppSettings, jsonSerializerOptions);
	}

	private static async Task WriteDockerScript()
	{
		if (File.Exists(Program.dockerScriptFile))
		{
			return;
		}

		StringBuilder builder = new();

		builder.AppendLine("docker run -d -p 4080:4080 --name firely.server `");
		builder.AppendLine($"-v ${{PWD}}/{Program.firelyLicenseFile}:/app/{Program.firelyLicenseFile} `");
		builder.AppendLine($"-v ${{PWD}}/{Program.firelyAppSettingsFile}:/app/{Program.firelyAppSettingsFile} `");
		builder.AppendLine("-v ${PWD}/vonk-import.R4:/app/vonk-import.R4 `");
		builder.AppendLine("firely/server");

		await using Stream stream = File.Open(Program.dockerScriptFile, FileMode.Create, FileAccess.Write);
		await using StreamWriter writer = new(stream);
		await writer.WriteAsync(builder.ToString());
	}
}